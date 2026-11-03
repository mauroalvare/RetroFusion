using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Retro.Ai;

/// <summary>
/// The official command-line tools that come with the AI subscriptions (no API key, no
/// separate billing): Claude Code with Claude Pro/Max, Codex CLI with ChatGPT Plus/Pro,
/// Antigravity CLI (or the older Gemini CLI) with a Google account. The user installs the
/// tool and signs in once; RetroEmu runs it in non-interactive mode and reads the answer.
/// RetroEmu never sees or stores the user's login.
/// </summary>
public sealed record CliTool(
    string Id,
    string Name,
    string Subscription,
    string[] Commands,
    string InstallHint);

public static class CliTools
{
    public static readonly CliTool Claude = new("sub-claude", "Claude Code", "Claude Pro o Max",
        new[] { "claude" },
        "Seguí https://code.claude.com/docs/en/quickstart para instalar Claude Code; después ejecutá claude e iniciá sesión con tu cuenta.");

    public static readonly CliTool Codex = new("sub-codex", "Codex CLI", "ChatGPT Plus o Pro",
        new[] { "codex" },
        "Seguí https://developers.openai.com/codex/cli/ para instalar Codex CLI; después ejecutá codex login y elegí iniciar sesión con ChatGPT.");

    public static readonly CliTool Google = new("sub-google", "Antigravity CLI", "una cuenta de Google (gratis con límites, o Google AI Pro/Ultra)",
        new[] { "agy", "gemini" },
        "Instalá Antigravity CLI (el comando agy) siguiendo las instrucciones de Google, escribí agy en una terminal y entrá con tu cuenta de Google.");

    public static readonly CliTool[] All = { Claude, Codex, Google };

    public static CliTool? ById(string id) => All.FirstOrDefault(t => t.Id == id);

    /// <summary>Full path of the tool: the user's override, or the first match on PATH and in the usual install folders.</summary>
    public static string? Find(CliTool tool, string? overridePath)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
            return File.Exists(overridePath) ? overridePath : null;
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).ToList();
        if (OperatingSystem.IsWindows())
            foreach (var scope in new[] { EnvironmentVariableTarget.User, EnvironmentVariableTarget.Machine })
                dirs.AddRange((Environment.GetEnvironmentVariable("PATH", scope) ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        dirs.Add(Path.Combine(home, ".local", "bin"));
        dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm"));
        dirs.Add(Path.Combine(home, ".npm-global", "bin"));
        // the official installers of Antigravity CLI leave agy.exe here, without touching PATH
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        dirs.Add(Path.Combine(local, "agy", "bin"));
        dirs.Add(Path.Combine(local, "Programs", "OpenAI", "Codex", "bin"));
        dirs.Add(Path.Combine(local, "Antigravity"));
        dirs.Add("/usr/local/bin");
        dirs.Add("/opt/homebrew/bin");
        string[] extensions = OperatingSystem.IsWindows() ? new[] { ".exe", ".cmd", ".bat" } : new[] { "" };
        foreach (var name in tool.Commands)
            foreach (var dir in dirs)
                foreach (var ext in extensions)
                {
                    try
                    {
                        string p = Path.Combine(dir.Trim('"'), name + ext);
                        if (File.Exists(p))
                            return p;
                    }
                    catch (ArgumentException)
                    {
                    }
                }
        return null;
    }
}

/// <summary>Asks one of the subscription tools in non-interactive mode (prompt through stdin, answer from stdout).</summary>
public sealed class CliAgentProvider : IAiProvider
{
    internal string ResourceKey => _tool.Id;
    public AiUsage? LastUsage { get; private set; }
    public bool SupportsImages => true;
    private readonly CliTool _tool;
    private readonly string? _override;
    private readonly TimeSpan _timeout;
    private readonly string? _model;
    private readonly string? _effort;
    private CliActivity? _activity;

    public CliActivity? Activity => Volatile.Read(ref _activity);

    /// <param name="model">The model the tool uses (its --model / -m); null or empty = the tool's own default.</param>
    public CliAgentProvider(CliTool tool, string? overridePath, TimeSpan? timeout = null, string? model = null, string? effort = null)
    {
        _tool = tool;
        _override = overridePath;
        _timeout = timeout ?? TimeSpan.FromMinutes(15);
        _model = string.IsNullOrWhiteSpace(model) ? null : model.Trim();
        _effort = string.IsNullOrWhiteSpace(effort) ? null : effort.Trim();
    }

    public string Name => _model != null ? Retro.Core.UiText.Format($"{Retro.Core.UiText.Raw(_tool.Name)} ({_model}, suscripción)") : Retro.Core.UiText.Format($"{Retro.Core.UiText.Raw(_tool.Name)} (suscripción)");

    /// <summary>The arguments that choose the model, for each tool.</summary>
    public static IEnumerable<string> ModelArgs(string toolId, string? model) =>
        string.IsNullOrWhiteSpace(model) ? Array.Empty<string>()
        : toolId == "sub-codex" ? new[] { "-m", model.Trim() }
        : new[] { "--model", model.Trim() };

    public static IEnumerable<string> EffortArgs(string toolId, string? effort)
    {
        if (string.IsNullOrWhiteSpace(effort)) return Array.Empty<string>();
        if (!System.Text.RegularExpressions.Regex.IsMatch(effort, "^[a-z][a-z0-9_-]{0,39}$"))
            throw new AiException("El esfuerzo guardado no es válido. Volvé a elegirlo en la lista.");
        return toolId == "sub-codex" ? new[] { "-c", Retro.Core.UiText.Format($"model_reasoning_effort='{effort}'") }
            : new[] { "--effort", effort };
    }

    /// <summary>The whole conversation as one text, since these tools take a single prompt.</summary>
    public static string Flatten(AiRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Retro.Core.UiText.Get("# Instrucciones"));
        sb.AppendLine(request.System);
        sb.AppendLine();
        sb.AppendLine(Retro.Core.UiText.Get("# Conversación"));
        foreach (var t in request.Turns)
        {
            sb.AppendLine(t.FromUser ? "## Usuario" : "## Vos (respuesta anterior)");
            sb.AppendLine(t.Text);
            sb.AppendLine();
        }
        sb.AppendLine(Retro.Core.UiText.Get("# Tu tarea"));
        if (request.Images is { Count: > 0 } images)
            sb.AppendLine(Retro.Core.UiText.Get("Respondé al último mensaje del usuario. Mirá estas capturas de pantalla (podés leerlas con tu herramienta de leer archivos): ") +
                          string.Join(", ", images) + Retro.Core.UiText.Get(". No uses otras herramientas ni escribas archivos: respondé solo con texto."));
        else
            sb.AppendLine(Retro.Core.UiText.Get("Respondé al último mensaje del usuario. No uses herramientas, no leas ni escribas archivos: respondé solo con texto."));
        if (request.Schema is JsonElement schema)
            sb.AppendLine(Retro.Core.UiText.Get("Tu respuesta tiene que ser SOLO un objeto JSON válido (sin texto antes ni después, sin bloques de código) que cumpla este esquema JSON:\n") +
                          schema.GetRawText());
        return sb.ToString();
    }

    public async Task<string> CompleteAsync(AiRequest request, CancellationToken ct)
    {
        LastUsage = null;
        ct.ThrowIfCancellationRequested();
        string exe = CliTools.Find(_tool, _override)
                     ?? throw new AiException(Retro.Core.UiText.Format($"No encontré {Retro.Core.UiText.Raw(_tool.Name)}. {_tool.InstallHint}"));
        string work = Path.Combine(Path.GetTempPath(), "RetroEmu-ia");
        Directory.CreateDirectory(work);
        string lastMessage = Path.Combine(work, Retro.Core.UiText.Format($"respuesta-{Guid.NewGuid():N}.txt"));
        string? schemaFile = null;
        try
        {
            bool agy = Path.GetFileNameWithoutExtension(exe).Equals("agy", StringComparison.OrdinalIgnoreCase);
            var images = request.Images ?? Array.Empty<string>();
            List<string> args = _tool.Id switch
            {
                // json output carries the text in "result" and tells failures (no login, limits) apart;
                // pictures are read with the Read tool, allowed only for their folder
                "sub-claude" => images.Count == 0
                    ? new() { "-p", "--output-format", "json", "--permission-mode", "dontAsk" }
                    : new List<string> { "-p", "--output-format", "json", "--permission-mode", "dontAsk", "--allowedTools", "Read" }
                        .Concat(images.Select(Path.GetDirectoryName).Distinct().SelectMany(d => new[] { "--add-dir", d! })).ToList(),
                // "-" = the prompt comes from stdin; read-only sandbox by default; no git repo needed; -i attaches pictures
                "sub-codex" => new List<string> { "exec", "--json", "--skip-git-repo-check", "-o", lastMessage }
                    .Concat(images.SelectMany(i => new[] { "-i", i })).Append("-").ToList(),
                _ => agy ? AgyArgs(request, work, out schemaFile)
                         : new() { "-p", "Respondé la consigna que te paso por la entrada estándar.", "--output-format", "json" },
            };
            // the model goes before codex's "-" (stdin) and anywhere for the others
            args.InsertRange(_tool.Id == "sub-codex" ? 1 : 0, ModelArgs(_tool.Id, _model));
            args.InsertRange(_tool.Id == "sub-codex" ? 1 : 0, EffortArgs(_tool.Id, _effort));
            if (_tool.Id == "sub-codex" && request.Schema is JsonElement schema)
            {
                schemaFile = Path.Combine(work, Retro.Core.UiText.Format($"esquema-{Guid.NewGuid():N}.json"));
                File.WriteAllText(schemaFile, schema.GetRawText(), new UTF8Encoding(false));
                args.InsertRange(args.Count - 1, ["--output-schema", schemaFile]);
            }
            // One stream-json user event carries the entire agy request, with no command-line size limit or file-reading tool.
            string previousOutput = Activity?.Output is { Length: > 0 } previous ? previous + "\n\n" : "";
            string input = agy ? AntigravityProtocol.Input(Flatten(request)) : Flatten(request);
            var (code, stdout, stderr) = await RunAsync(exe, args, work, input, _timeout, ct,
                activity => Volatile.Write(ref _activity, activity with { Output = CliActivity.Tail(previousOutput +
                    (_tool.Id == "sub-codex" ? CliActivity.CodexText(activity.Output) : activity.Output)) }));
            LastUsage = AiUsage.Cli(_tool.Id, stdout);
            if (code != 0)
                throw new AiException(Failure(stderr.Length > 0 ? stderr : stdout));
            string answer = _tool.Id switch
            {
                "sub-claude" => ClaudeResult(stdout, stderr, code),
                "sub-codex" => File.Exists(lastMessage) ? File.ReadAllText(lastMessage) : stdout,
                _ => agy ? AgyResult(stdout, stderr) : GeminiResult(stdout),
            };
            if (string.IsNullOrWhiteSpace(answer))
                throw new AiException(Retro.Core.UiText.Format($"{Retro.Core.UiText.Raw(_tool.Name)} no devolvió ninguna respuesta. {Tail(stderr)}"));
            return answer;
        }
        finally
        {
            foreach (string? file in new[] { lastMessage, schemaFile })
                if (file != null)
                    try { File.Delete(file); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// Antigravity CLI: stdin receives one NDJSON user event; the CLI reads the schema itself,
    /// without asking the agent to read a prompt file or granting command permissions.
    /// </summary>
    private static List<string> AgyArgs(AiRequest request, string work, out string? schemaFile)
    {
        schemaFile = null;
        var dirs = (request.Images ?? Array.Empty<string>()).Select(i => Path.GetDirectoryName(i)!).ToList();
        var args = new List<string> { "--input-format", "stream-json", "--output-format", "stream-json", "--mode", "plan", "--disable-slash-commands" };
        if (request.Schema is JsonElement schema)
        {
            schemaFile = Path.Combine(work, $"esquema-{Guid.NewGuid():N}.json");
            File.WriteAllText(schemaFile, schema.GetRawText(), new UTF8Encoding(false));
            args.AddRange(new[] { "--json-schema", schemaFile });
        }
        foreach (var d in dirs.Distinct()) args.AddRange(new[] { "--add-dir", d });
        return args;
    }

    private string ClaudeResult(string stdout, string stderr, int code)
    {
        int s = stdout.IndexOf('{');
        if (s < 0)
            throw new AiException(Failure(stderr.Length > 0 ? stderr : stdout));
        try
        {
            using var doc = JsonDocument.Parse(stdout[s..]);
            var root = doc.RootElement;
            string result = root.TryGetProperty("result", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() ?? "" : "";
            bool isError = root.TryGetProperty("is_error", out var e) && e.ValueKind == JsonValueKind.True;
            if (isError || code != 0)
                throw new AiException(Failure(result.Length > 0 ? result : stderr));
            return result;
        }
        catch (JsonException)
        {
            return stdout;
        }
    }

    /// <summary>Only a final result can complete the request; progress and partial text cannot.</summary>
    private string AgyResult(string stdout, string stderr)
    {
        if (AntigravityProtocol.Result(stdout) is not { } root)
            throw new AiException(AntigravityProtocol.PermissionDenied(stderr) ? Failure(stderr)
                : Retro.Core.UiText.Get("Antigravity CLI no confirmó una respuesta completa. La salida parcial no se aplicó. Podés reintentar o usar el modo manual."));
        string response = AntigravityProtocol.Text(root, "response");
        if (!AntigravityProtocol.Text(root, "status").Equals("SUCCESS", StringComparison.OrdinalIgnoreCase))
        {
            string error = AntigravityProtocol.Text(root, "error");
            throw new AiException(Failure(error.Length > 0 ? error : response.Length > 0 ? response : stdout));
        }
        if (root.TryGetProperty("structured_output", out var so) && so.ValueKind == JsonValueKind.Object) return so.GetRawText();
        if (string.IsNullOrWhiteSpace(response) && AntigravityProtocol.PermissionDenied(stderr)) throw new AiException(Failure(stderr));
        return response;
    }

    private static string GeminiResult(string stdout)
    {
        int s = stdout.IndexOf('{');
        if (s < 0)
            return stdout;
        try
        {
            using var doc = JsonDocument.Parse(stdout[s..]);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.Object)
                throw new AiException(Retro.Core.UiText.Get("Error de la IA: ") + err.GetRawText());
            return root.TryGetProperty("response", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() ?? "" : stdout;
        }
        catch (JsonException)
        {
            return stdout;
        }
    }

    /// <summary>A failure message that tells the user what to do.</summary>
    private string Failure(string output)
    {
        if (_tool.Id == "sub-google" && AntigravityProtocol.PermissionDenied(output))
            return Retro.Core.UiText.Get("Antigravity CLI bloqueó una herramienta porque no puede pedir permiso en modo sin interacción. Revisá /permissions en una sesión interactiva y autorizá solo la acción concreta que quieras permitir, o usá el modo manual. RetroEmu no habilita permisos generales automáticamente.");
        if (_tool.Id == "sub-google" && output.Contains("input-format", StringComparison.OrdinalIgnoreCase)
            && (output.Contains("unknown", StringComparison.OrdinalIgnoreCase) || output.Contains("not defined", StringComparison.OrdinalIgnoreCase)))
            return Retro.Core.UiText.Get("Esta versión de Antigravity CLI no admite la entrada por stream-json. Revisá Ayuda > Conectar IA para actualizarla desde Google, o usá el modo manual. RetroEmu no descargó ni instaló herramientas.");
        string text = Tail(output);
        string lower = text.ToLowerInvariant();
        if (lower.Contains("login") || lower.Contains("log in") || lower.Contains("sign in") || lower.Contains("auth") || lower.Contains("credential"))
            return Retro.Core.UiText.Format($"{Retro.Core.UiText.Raw(_tool.Name)} no tiene una sesión iniciada. Abrí una terminal, ejecutalo y entrá con tu cuenta de {_tool.Subscription}. Detalle: {text}");
        if (lower.Contains("limit") || lower.Contains("quota") || lower.Contains("rate"))
            return Retro.Core.UiText.Format($"Llegaste al límite de uso de tu plan en {Retro.Core.UiText.Raw(_tool.Name)}; probá más tarde. Detalle: {text}");
        return Retro.Core.UiText.Format($"{Retro.Core.UiText.Raw(_tool.Name)} falló: {text}");
    }

    private static string Tail(string s)
    {
        s = s.Trim();
        return s.Length > 600 ? "..." + s[^600..] : s;
    }

    internal static async Task<(int code, string stdout, string stderr)> RunAsync(string exe, IReadOnlyList<string> args, string workDir,
        string stdin, TimeSpan timeout, CancellationToken ct, Action<CliActivity>? activity = null)
    {
        var psi = StartInfo(exe, args, workDir);
        using var p = new Process { StartInfo = psi };
        try
        {
            p.Start();
        }
        catch (Exception e)
        {
            throw new AiException(Retro.Core.UiText.Format($"No se pudo ejecutar {exe}: {e.Message}"));
        }
        long started = Stopwatch.GetTimestamp(), lastOutput = started, characters = 0;
        var output = new StringBuilder();
        var gate = new object();
        string status = stdin.Length > 0 ? "Enviando pedido al CLI" : "CLI en ejecución";
        bool inputDelivered = false;
        void Report(bool running)
        {
            lock (gate)
                activity?.Invoke(new CliActivity(status, running, started, Stopwatch.GetTimestamp(), lastOutput, characters, output.ToString()));
        }
        async Task<string> ReadAsync(StreamReader reader)
        {
            var answer = new StringBuilder();
            var buffer = new char[2048];
            int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory(), ct)) > 0)
            {
                answer.Append(buffer, 0, count);
                lock (gate)
                {
                    characters += count;
                    lastOutput = Stopwatch.GetTimestamp();
                    output.Append(buffer, 0, count);
                    if (output.Length > CliActivity.MaxOutputLength)
                        output.Remove(0, output.Length - CliActivity.MaxOutputLength);
                    Report(true);
                }
            }
            return answer.ToString();
        }
        Report(true);
        var outTask = ReadAsync(p.StandardOutput);
        var errTask = ReadAsync(p.StandardError);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        try
        {
            try
            {
                if (stdin.Length > 0)
                {
                    await p.StandardInput.WriteAsync(stdin.AsMemory(), timeoutCts.Token);
                    // Flush with cancellation before Close can perform a synchronous write.
                    await p.StandardInput.FlushAsync(timeoutCts.Token);
                }
                p.StandardInput.Close();
                if (stdin.Length > 0)
                {
                    inputDelivered = true;
                    lock (gate) status = "Pedido entregado al CLI; esperando respuesta";
                    Report(true);
                }
            }
            catch (IOException) { }
            await p.WaitForExitAsync(timeoutCts.Token);
            string stdout = await outTask, stderr = await errTask;
            status = p.ExitCode == 0 ? "CLI finalizado" : "CLI terminó con error";
            return (p.ExitCode, stdout, stderr);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            try { await Task.WhenAll(outTask, errTask); } catch (OperationCanceledException) { }
            status = ct.IsCancellationRequested ? "CLI cancelado" : "CLI detenido por tiempo máximo";
            if (ct.IsCancellationRequested) throw;
            string limit = timeout.TotalMinutes >= 1 ? Retro.Core.UiText.Format($"{timeout.TotalMinutes:0.#} minutos") : Retro.Core.UiText.Format($"{Math.Max(1, timeout.TotalSeconds):0.#} segundos");
            string phase = stdin.Length == 0 ? "" : inputDelivered
                ? " El pedido llegó al CLI, pero no se confirmó una respuesta dentro del plazo. Revisá Modelo y conexión → Conexión avanzada."
                : " No terminó de enviar el pedido al CLI. Revisá la ruta de la herramienta en Modelo y conexión → Conexión avanzada.";
            throw new AiException(Retro.Core.UiText.Format($"{Retro.Core.UiText.Raw(Path.GetFileName(exe))} tardó más de {limit} y lo detuve.") + phase);
        }
        finally { Report(false); }
    }

    internal static ProcessStartInfo StartInfo(string exe, IReadOnlyList<string> args, string workDir)
    {
        var psi = new ProcessStartInfo
        {
            WorkingDirectory = workDir,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            // the prompt goes in UTF-8 without BOM: on Windows the default is the console code
            // page, and the tools reject (Codex) or garble the accents of a Spanish prompt
            StandardInputEncoding = new UTF8Encoding(false),
        };
        string ext = Path.GetExtension(exe).ToLowerInvariant();
        if (ext is ".cmd" or ".bat")
        {
            // npm installs these tools as .cmd shims, which only cmd.exe can run
            psi.FileName = "cmd.exe";
            psi.Arguments = Retro.Core.UiText.Get("/d /s /c \"") + Quote(exe) + " " + string.Join(" ", args.Select(Quote)) + "\"";
        }
        else
        {
            psi.FileName = exe;
            foreach (var a in args)
                psi.ArgumentList.Add(a);
        }
        psi.Environment["NO_COLOR"] = "1";
        return psi;
    }

    private static string Quote(string a) => a.Length > 0 && a.IndexOfAny(new[] { ' ', '"', '&', '|', '<', '>', '^' }) < 0 ? a : "\"" + a.Replace("\"", "\\\"") + "\"";

    /// <summary>A tiny request to check that the tool is installed and signed in.</summary>
    public async Task<string> TestAsync(CancellationToken ct)
    {
        string answer = await CompleteAsync(new AiRequest("Sos un asistente de prueba.", new[] { new AiTurn(true, "Respondé solo con la palabra: listo") }, null), ct);
        return answer.Trim();
    }
}
