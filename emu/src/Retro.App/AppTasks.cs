using System.Globalization;

namespace Retro.App;

/// <summary>
/// What a long task is doing. Waiting for a provider, for the user or for a resource are different from
/// working locally, and each ending says how much was achieved.
/// </summary>
public enum AppTaskState { Queued, Working, WaitingProvider, WaitingUser, Paused, Blocked, Completed, PartlyCompleted, Failed, Cancelled }

/// <summary>What a workshop reports about its running job, read once per frame.</summary>
public sealed record WorkReport(string Kind, string Target, string Stage, AppTaskState State = AppTaskState.Working,
    long? Done = null, long? Total = null, string Unit = "", Action? Cancel = null);

internal sealed record WorkSnapshot(WorkReport? Report, AppTaskState Ending, string Detail);

/// <summary>A long operation. Measured progress is optional: without a real count there is no percentage.</summary>
public sealed class AppTask
{
    private const int MaxLines = 256;
    private readonly object _gate = new();
    private readonly List<string> _results = [], _errors = [], _artifacts = [];
    private readonly TimeProvider _clock;
    private CancellationTokenSource? _cancellation;
    private Action? _cancel, _resume;
    public int Id { get; }
    public string Kind { get; }
    public string Target { get; }
    public DateTimeOffset Started { get; }
    public AppTaskState State { get; private set; }
    public string Stage { get; private set; } = "";
    public DateTimeOffset LastActivity { get; private set; }
    public DateTimeOffset? Finished { get; private set; }
    public long? Done { get; private set; }
    public long? Total { get; private set; }
    public string Unit { get; private set; } = "";
    public bool CancelRequested { get; private set; }
    public bool Partial { get; set; }
    /// <summary>Owned by a workshop that already reports its own work when the program closes.</summary>
    public bool Tracked { get; internal set; }
    public bool Active => Finished == null;
    public bool CanCancel { get { lock (_gate) return Active && !CancelRequested && (_cancel != null || _cancellation != null); } }
    public bool CanResume { get { lock (_gate) return !Active && _resume != null; } }
    public IReadOnlyList<string> Results { get { lock (_gate) return _results.ToArray(); } }
    public IReadOnlyList<string> Errors { get { lock (_gate) return _errors.ToArray(); } }
    public IReadOnlyList<string> Artifacts { get { lock (_gate) return _artifacts.ToArray(); } }
    public int Revision { get; private set; }

    internal AppTask(int id, string kind, string target, TimeProvider clock, CancellationTokenSource? cancellation, Action? cancel, Action? resume)
    {
        Id = id; Kind = kind; Target = target; _clock = clock; _cancellation = cancellation; _cancel = cancel; _resume = resume;
        Started = LastActivity = clock.GetUtcNow(); State = AppTaskState.Queued;
    }

    public TimeSpan Elapsed => (Finished ?? _clock.GetUtcNow()) - Started;
    public TimeSpan Quiet => _clock.GetUtcNow() - LastActivity;
    public double? Fraction => Done is { } done && Total is > 0 and var total ? Math.Clamp((double)done / total, 0, 1) : null;

    /// <summary>Records the current stage. Anything that changes counts as activity; repeating the same report does not.</summary>
    public void Report(string stage, AppTaskState state = AppTaskState.Working, long? done = null, long? total = null, string unit = "")
    {
        lock (_gate)
        {
            if (Finished != null) return;
            bool changed = stage != Stage || state != State || done != Done || total != Total;
            Stage = stage; State = state; Done = done; Total = total; Unit = unit;
            if (changed) { LastActivity = _clock.GetUtcNow(); Revision++; }
        }
    }

    public void Result(string line) => Add(_results, line);
    public void Error(string line) => Add(_errors, line);
    public void Artifact(string path) => Add(_artifacts, path);
    private void Add(List<string> list, string line)
    {
        lock (_gate)
        {
            if (list.Count >= MaxLines) list.RemoveAt(0);
            list.Add(line); LastActivity = _clock.GetUtcNow(); Revision++;
        }
    }

    public void SetCancel(Action? cancel) { lock (_gate) _cancel = cancel; }

    public bool Cancel()
    {
        Action? cancel; CancellationTokenSource? source;
        lock (_gate)
        {
            if (!CanCancel) return false;
            CancelRequested = true; cancel = _cancel; source = _cancellation; Stage = "Cancelando…"; Revision++;
        }
        source?.Cancel(); cancel?.Invoke();
        return true;
    }

    public bool Resume()
    {
        Action resume;
        lock (_gate)
        {
            if (Active || _resume == null) return false;
            resume = _resume; _resume = null; Revision++;
        }
        try { resume(); return true; }
        catch
        {
            lock (_gate) { _resume = resume; Revision++; }
            throw;
        }
    }

    internal void Finish(AppTaskState state, Action? resume = null)
    {
        lock (_gate)
        {
            if (Finished != null) return;
            State = state; Finished = _clock.GetUtcNow(); _cancellation = null; _cancel = null; _resume = resume; Revision++;
        }
    }
}

/// <summary>All long operations of the session, whichever workshop or order started them.</summary>
public sealed class AppTaskCenter(TimeProvider? clock = null)
{
    internal static AppTaskState FinishedState(Task task) => task.IsCanceled || task.Exception?.GetBaseException() is OperationCanceledException
        ? AppTaskState.Cancelled : task.IsFaulted ? AppTaskState.Failed : task.IsCompletedSuccessfully ? AppTaskState.Completed : AppTaskState.PartlyCompleted;
    private const int KeepFinished = 64;
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly object _gate = new();
    private readonly List<AppTask> _tasks = [];
    private readonly Dictionary<string, (AppTask Task, WorkReport Last)> _tracked = [];
    private readonly Queue<AppTask> _finished = new();
    private int _nextId = 1;
    /// <summary>Called on the interface thread, from Poll.</summary>
    public event Action<AppTask>? TaskFinished;
    public TimeSpan LocalQuietLimit { get; set; } = TimeSpan.FromSeconds(45);
    public TimeSpan ProviderQuietLimit { get; set; } = TimeSpan.FromMinutes(4);

    public IReadOnlyList<AppTask> All { get { lock (_gate) return _tasks.ToArray(); } }
    public IReadOnlyList<AppTask> Active { get { lock (_gate) return _tasks.Where(task => task.Active).ToArray(); } }
    public AppTask? Find(int id) { lock (_gate) return _tasks.FirstOrDefault(task => task.Id == id); }

    private AppTask Add(string kind, string target, CancellationTokenSource? cancellation, Action? cancel, Action? resume)
    {
        lock (_gate)
        {
            var task = new AppTask(_nextId++, kind, target, _clock, cancellation, cancel, resume);
            _tasks.Add(task);
            int excess = _tasks.Count(item => !item.Active) - KeepFinished;
            if (excess > 0) _tasks.RemoveAll(item => !item.Active && excess-- > 0);
            return task;
        }
    }

    /// <summary>Runs work off the interface thread. The body reports; it never touches the interface.</summary>
    public AppTask Run(string kind, string target, Func<AppTask, CancellationToken, Task> work, Action? resume = null)
    {
        var cancellation = new CancellationTokenSource();
        var task = Add(kind, target, cancellation, null, resume);
        var token = cancellation.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await work(task, token).ConfigureAwait(false);
                task.Finish(task.Partial ? AppTaskState.PartlyCompleted : AppTaskState.Completed);
            }
            catch (OperationCanceledException) { task.Finish(AppTaskState.Cancelled, resume); }
            catch (Exception error) when (error is not OutOfMemoryException)
            { task.Error(error.Message); task.Finish(AppTaskState.Failed, resume); }
            finally
            {
                cancellation.Dispose();
                lock (_gate) _finished.Enqueue(task);
            }
        });
        return task;
    }

    /// <summary>A disappearing report alone cannot prove success. The owner must supply its observed ending.</summary>
    public void Track(string key, WorkReport? report, AppTaskState ending = AppTaskState.PartlyCompleted, string? detail = null)
    {
        void Finish(AppTask task)
        {
            var state = task.CancelRequested ? AppTaskState.Cancelled : ending;
            if (!string.IsNullOrWhiteSpace(detail)) { if (state == AppTaskState.Failed) task.Error(detail); else task.Result(detail); }
            else if (state == AppTaskState.PartlyCompleted) task.Result(UiText.Get("El proceso dejó de informar el trabajo; no se confirmó que esté terminado."));
            task.Finish(state);
            lock (_gate) _finished.Enqueue(task);
        }
        if (report == null)
        {
            if (_tracked.Remove(key, out var ended))
                Finish(ended.Task);
            return;
        }
        if (!_tracked.TryGetValue(key, out var entry) || entry.Last.Kind != report.Kind || entry.Last.Target != report.Target)
        {
            if (entry.Task != null) Finish(entry.Task);
            entry.Task = Add(report.Kind, report.Target, null, report.Cancel, null);
            entry.Task.Tracked = true;
        }
        entry.Task.SetCancel(report.Cancel);
        entry.Task.Report(report.Stage, report.State, report.Done, report.Total, report.Unit);
        _tracked[key] = (entry.Task, report);
    }

    /// <summary>Delivers endings on the interface thread.</summary>
    public void Poll()
    {
        while (true)
        {
            AppTask task;
            lock (_gate)
            {
                if (_finished.Count == 0) return;
                task = _finished.Dequeue();
            }
            TaskFinished?.Invoke(task);
        }
    }

    public void ClearFinished() { lock (_gate) _tasks.RemoveAll(task => !task.Active); }

    /// <summary>A task that should have shown activity by now. Waiting for the user or a paused task never counts.</summary>
    public bool IsQuiet(AppTask task) => task.Active && task.State is AppTaskState.Working or AppTaskState.WaitingProvider or AppTaskState.Queued
        && task.Quiet > (task.State == AppTaskState.WaitingProvider ? ProviderQuietLimit : LocalQuietLimit);

    /// <summary>What the user can read about the state, without a number that nothing measured.</summary>
    public string Describe(AppTask task)
    {
        string state = StateName(task.State);
        if (!IsQuiet(task)) return state;
        return state + UiText.Format($" · sin actividad hace {Duration(task.Quiet)}");
    }

    public string Advice(AppTask task)
    {
        if (!IsQuiet(task)) return "";
        string reason = task.State == AppTaskState.WaitingProvider ? "Sigue esperando la respuesta del proveedor; puede tardar o haberse cortado." : "El proceso no informó avances.";
        return UiText.Get(reason) + UiText.Get(task.CanCancel ? " Podés cancelarla; los resultados parciales se conservan." : " No se puede cancelar desde acá.");
    }

    public static string StateName(AppTaskState state) => UiText.Get(state switch
    {
        AppTaskState.Queued => "en cola",
        AppTaskState.Working => "trabajo local",
        AppTaskState.WaitingProvider => "esperando al proveedor",
        AppTaskState.WaitingUser => "esperando tu respuesta",
        AppTaskState.Paused => "en pausa",
        AppTaskState.Blocked => "bloqueada por un recurso",
        AppTaskState.Completed => "terminada",
        AppTaskState.PartlyCompleted => "terminada en parte",
        AppTaskState.Failed => "falló",
        _ => "cancelada"
    });

    public static string Duration(TimeSpan time) =>
        time.TotalHours >= 1 ? ((int)time.TotalHours).ToString(CultureInfo.InvariantCulture) + UiText.Get(" h ") + time.Minutes + UiText.Get(" min")
        : time.TotalMinutes >= 1 ? ((int)time.TotalMinutes).ToString(CultureInfo.InvariantCulture) + UiText.Get(" min ") + time.Seconds + UiText.Get(" s")
        : Math.Max(0, (int)time.TotalSeconds).ToString(CultureInfo.InvariantCulture) + UiText.Get(" s");

    /// <summary>"12 de 40 juegos" when a count exists; empty when progress cannot be measured.</summary>
    public static string Progress(AppTask task) => task.Done is { } done
        ? task.Total is { } total ? UiText.Format($"{done} de {total} {task.Unit}").TrimEnd() : UiText.Format($"{done} {task.Unit}").TrimEnd()
        : "";
}
