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
        "Instalá Claude Code (claude.com/claude-code, o en una terminal: npm install -g @anthropic-ai/claude-code), escribí claude en una terminal y entrá con tu cuenta de Claude.");

    public static readonly CliTool Codex = new("sub-codex", "Codex CLI", "ChatGPT Plus o Pro",
        new[] { "codex" },
        "Instalá Codex CLI (en una terminal: npm install -g @openai/codex), escribí codex login y elegí entrar con tu cuenta de ChatGPT.");

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
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        dirs.Add(Path.Combine(home, ".local", "bin"));
        dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm"));
        dirs.Add(Path.Combine(home, ".npm-global", "bin"));
        // the official installers of Antigravity CLI leave agy.exe here, without touching PATH
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        dirs.Add(Path.Combine(local, "agy", "bin"));
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
    private readonly CliTool _tool;
    private readonly string? _override;
    private readonly TimeSpan _timeout;

    public CliAgentProvider(CliTool tool, string? overridePath, TimeSpan? timeout = null)
    {
        _tool = tool;
        _override = overridePath;
        _timeout = timeout ?? TimeSpan.FromMinutes(15);
    }

    public string Name => $"{_tool.Name} (suscripción)";

    /// <summary>The whole conversation as one text, since these tools take a single prompt.</summary>
    public static string Flatten(AiRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Instrucciones");
        sb.AppendLine(request.System);
        sb.AppendLine();
        sb.AppendLine("# Conversación");
        foreach (var t in request.Turns)
        {
            sb.AppendLine(t.FromUser ? "## Usuario" : "## Vos (respuesta anterior)");
            sb.AppendLine(t.Text);
            sb.AppendLine();
        }
        sb.AppendLine("# Tu tarea");
        if (request.Images is { Count: > 0 } images)
            sb.AppendLine("Respondé al último mensaje del usuario. Mirá estas capturas de pantalla (podés leerlas con tu herramienta de leer archivos): " +
                          string.Join(", ", images) + ". No uses otras herramientas ni escribas archivos: respondé solo con texto.");
        else
            sb.AppendLine("Respondé al último mensaje del usuario. No uses herramientas, no leas ni escribas archivos: respondé solo con texto.");
        if (request.Schema is JsonElement schema)
            sb.AppendLine("Tu respuesta tiene que ser SOLO un objeto JSON válido (sin texto antes ni después, sin bloques de código) que cumpla este esquema JSON:\n" +
                          schema.GetRawText());
        return sb.ToString();
    }

    public async Task<string> CompleteAsync(AiRequest request, CancellationToken ct)
    {
        string exe = CliTools.Find(_tool, _override)
                     ?? throw new AiException($"No encontré {_tool.Name}. {_tool.InstallHint}");
        string work = Path.Combine(Path.GetTempPath(), "RetroEmu-ia");
        Directory.CreateDirectory(work);
        string lastMessage = Path.Combine(work, $"respuesta-{Guid.NewGuid():N}.txt");
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
            "sub-codex" => new List<string> { "exec", "--skip-git-repo-check", "-o", lastMessage }
                .Concat(images.SelectMany(i => new[] { "-i", i })).Append("-").ToList(),
            // the -p text is appended to stdin, which carries the real prompt
            _ => agy ? AgyArgs(request, work)
                     : new() { "-p", "Respondé la consigna que te paso por la entrada estándar.", "--output-format", "json" },
        };
        // agy takes the prompt as an argument and does not read standard input
        var (code, stdout, stderr) = await RunAsync(exe, args, work, agy ? "" : Flatten(request), _timeout, ct);
        try
        {
            string answer = _tool.Id switch
            {
                "sub-claude" => ClaudeResult(stdout, stderr, code),
                "sub-codex" => File.Exists(lastMessage) ? File.ReadAllText(lastMessage) : stdout,
                _ => agy ? AgyResult(stdout) : GeminiResult(stdout),
            };
            if (code != 0 && string.IsNullOrWhiteSpace(answer))
                throw new AiException(Failure(stderr.Length > 0 ? stderr : stdout));
            if (string.IsNullOrWhiteSpace(answer))
                throw new AiException($"{_tool.Name} no devolvió ninguna respuesta. {Tail(stderr)}");
            return answer;
        }
        finally
        {
            try { File.Delete(lastMessage); } catch (IOException) { }
        }
    }

    /// <summary>
    /// Antigravity CLI: the prompt goes as the -p argument (in a file it is told to read when it
    /// does not fit in a Windows command line), the schema with --json-schema, and the folders
    /// of the pictures and of that file in its workspace, in the read-only plan mode.
    /// </summary>
    private static List<string> AgyArgs(AiRequest request, string work)
    {
        string prompt = Flatten(request);
        var dirs = (request.Images ?? Array.Empty<string>()).Select(i => Path.GetDirectoryName(i)!).ToList();
        if (prompt.Length > 24000)
        {
            string file = Path.Combine(work, $"consigna-{Guid.NewGuid():N}.md");
            File.WriteAllText(file, prompt, new UTF8Encoding(false));
            dirs.Add(work);
            prompt = $"La consigna completa está en el archivo {file}: leelo entero y hacé exactamente lo que pide (respondé solo con lo que pide).";
        }
        var args = new List<string> { "-p", prompt, "--output-format", "json" };
        if (request.Schema is JsonElement schema)
            args.AddRange(new[] { "--json-schema", schema.GetRawText() });
        if (dirs.Count > 0)
        {
            args.AddRange(new[] { "--mode", "plan" });
            foreach (var d in dirs.Distinct())
                args.AddRange(new[] { "--add-dir", d });
        }
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

    /// <summary>agy --output-format json: {"status":"SUCCESS","response":"...","structured_output":{...}}.</summary>
    private string AgyResult(string stdout)
    {
        int s = stdout.IndexOf('{');
        if (s < 0)
            return stdout;
        try
        {
            using var doc = JsonDocument.Parse(stdout[s..]);
            var root = doc.RootElement;
            string status = root.TryGetProperty("status", out var st) && st.ValueKind == JsonValueKind.String ? st.GetString() ?? "" : "";
            string response = root.TryGetProperty("response", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() ?? "" : "";
            if (status.Length > 0 && !status.Equals("SUCCESS", StringComparison.OrdinalIgnoreCase))
                throw new AiException(Failure(response.Length > 0 ? response : stdout));
            if (root.TryGetProperty("structured_output", out var so) && so.ValueKind == JsonValueKind.Object)
                return so.GetRawText();
            return response;
        }
        catch (JsonException)
        {
            return stdout;
        }
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
                throw new AiException("Error de la IA: " + err.GetRawText());
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
        string text = Tail(output);
        string lower = text.ToLowerInvariant();
        if (lower.Contains("login") || lower.Contains("log in") || lower.Contains("sign in") || lower.Contains("auth") || lower.Contains("credential"))
            return $"{_tool.Name} no tiene una sesión iniciada. Abrí una terminal, ejecutalo y entrá con tu cuenta de {_tool.Subscription}. Detalle: {text}";
        if (lower.Contains("limit") || lower.Contains("quota") || lower.Contains("rate"))
            return $"Llegaste al límite de uso de tu plan en {_tool.Name}; probá más tarde. Detalle: {text}";
        return $"{_tool.Name} falló: {text}";
    }

    private static string Tail(string s)
    {
        s = s.Trim();
        return s.Length > 600 ? "..." + s[^600..] : s;
    }

    internal static async Task<(int code, string stdout, string stderr)> RunAsync(string exe, IReadOnlyList<string> args, string workDir,
        string stdin, TimeSpan timeout, CancellationToken ct)
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
            psi.Arguments = "/d /s /c \"" + Quote(exe) + " " + string.Join(" ", args.Select(Quote)) + "\"";
        }
        else
        {
            psi.FileName = exe;
            foreach (var a in args)
                psi.ArgumentList.Add(a);
        }
        psi.Environment["NO_COLOR"] = "1";
        using var p = new Process { StartInfo = psi };
        try
        {
            p.Start();
        }
        catch (Exception e)
        {
            throw new AiException($"No se pudo ejecutar {exe}: {e.Message}");
        }
        var outTask = p.StandardOutput.ReadToEndAsync(ct);
        var errTask = p.StandardError.ReadToEndAsync(ct);
        try
        {
            if (stdin.Length > 0)
                await p.StandardInput.WriteAsync(stdin.AsMemory(), ct);
            p.StandardInput.Close();
        }
        catch (IOException)
        {
            // the tool closed its input without reading it: its answer (or error) is still in the output
        }
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        try
        {
            await p.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            if (ct.IsCancellationRequested)
                throw;
            throw new AiException($"{Path.GetFileName(exe)} tardó más de {timeout.TotalMinutes:F0} minutos y lo detuve.");
        }
        return (p.ExitCode, await outTask, await errTask);
    }

    private static string Quote(string a) => a.Length > 0 && a.IndexOfAny(new[] { ' ', '"', '&', '|', '<', '>', '^' }) < 0 ? a : "\"" + a.Replace("\"", "\\\"") + "\"";

    /// <summary>A tiny request to check that the tool is installed and signed in.</summary>
    public async Task<string> TestAsync(CancellationToken ct)
    {
        string answer = await CompleteAsync(new AiRequest("Sos un asistente de prueba.", new[] { new AiTurn(true, "Respondé solo con la palabra: listo") }, null), ct);
        return answer.Trim();
    }
}
