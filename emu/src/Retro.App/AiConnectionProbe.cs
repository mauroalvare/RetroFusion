namespace Retro.App;

/// <summary>One explicit connection check, owned and observed by the UI thread.</summary>
internal sealed class AiConnectionProbe
{
    private Task<string>? _pending;
    private CancellationTokenSource? _cancellation;
    public bool Busy => _pending != null;
    public bool IsCancelling => _cancellation?.IsCancellationRequested == true;
    public string Key { get; private set; } = "";
    public string Result { get; private set; } = "";

    public bool Start(string key, Func<CancellationToken, Task<string>> check)
    {
        Poll();
        if (Busy) return false;
        Key = key;
        Result = "";
        _cancellation = new();
        var token = _cancellation.Token;
        _pending = Task.Run(() => check(token), token);
        return true;
    }

    public void Cancel() => _cancellation?.Cancel();
    public Task CancelWork() { Cancel(); return (Task?)_pending ?? Task.CompletedTask; }

    public void Poll()
    {
        if (_pending is not { IsCompleted: true } completed) return;
        Result = completed.IsCanceled ? "Prueba cancelada. No se iniciará otro pedido."
            : completed.IsCompletedSuccessfully ? $"Funciona. Respondió: \"{completed.Result}\""
            : "No funcionó: " + completed.Exception!.GetBaseException().Message;
        _pending = null;
        _cancellation!.Dispose();
        _cancellation = null;
    }
}
