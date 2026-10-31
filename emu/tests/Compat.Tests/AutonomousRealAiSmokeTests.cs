using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Retro.Ai;
using Retro.Compat;
using Retro.Fusion;

namespace Compatibility.Tests;

/// <summary>An explicitly authorized, single-use CLI campaign; ordinary test runs never spend AI quota.</summary>
[TestFixture, NonParallelizable]
public sealed class AutonomousRealAiSmokeTests
{
    private sealed class NotRun : Exception;
    private sealed class Phase
    {
        public string Stage { get; set; } = "";
        public bool Parsed { get; set; }
        public int? ReturnedItems { get; set; }
        public double Seconds { get; set; }
        public string Failure { get; set; } = "";
    }

    private sealed class Evidence
    {
        public string Campaign { get; set; } = "T127 single CLI campaign";
        public DateTime StartedUtc { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedUtc { get; set; }
        public string Status { get; set; } = "starting";
        public string Provider { get; set; } = "";
        public string FixtureSha256 { get; set; } = "";
        public int Minutes { get; set; } = 3;
        public int MaxAiCalls { get; set; } = 2;
        public long MaxTextCharacters { get; set; }
        public int MaxPasses { get; set; } = 1;
        public int LabFrames { get; set; } = 120;
        public int TargetWriteEffects { get; set; } = 2;
        public string EngineStatus { get; set; } = "";
        public string StopReason { get; set; } = "";
        public int CompletedPasses { get; set; }
        public int Candidates { get; set; }
        public int VerifiedWriteEffects { get; set; }
        public AiOperationStats? Usage { get; set; }
        public List<Phase> Phases { get; set; } = [];
        public List<AutonomousResearchProgress> Progress { get; set; } = [];
        public List<string> Observations { get; set; } = [];
        public string Failure { get; set; } = "";
        public List<string> Limitations { get; set; } =
        [
            "Solo programa NROM sintético y perfil temporales; no se probaron juegos comerciales.",
            "Parsear una respuesta no demuestra que sus hipótesis sean correctas ni que cubran el objetivo.",
            "Los efectos de escritura observados no certifican el significado de las variables.",
            "Los tokens sin metadata del proveedor quedan desconocidos; la estimación de texto no los reemplaza.",
            "Esta campaña no crea una fusión ni certifica diversión, fidelidad o calidad de una creación.",
        ];
    }

    private sealed class StopAfterFailureAdvisor(AiPlayAdvisor inner, AiOperation operation, Evidence evidence, Action save)
        : IPlayAdvisor, IVariableNamer, IPreparationExperimentAdvisor
    {
        private bool _failed;
        public string Name => inner.Name;
        private async Task<T> Invoke<T>(string stage, Func<Task<T>> run, Func<T, int> count)
        {
            if (_failed) throw new AiException("La campaña se detuvo tras el primer fallo; no se repitió el proveedor.");
            var phase = new Phase { Stage = stage };
            evidence.Phases.Add(phase); save();
            var timing = Stopwatch.StartNew();
            try
            {
                T result = await run();
                phase.Parsed = true; phase.ReturnedItems = count(result);
                return result;
            }
            catch (Exception error)
            {
                _failed = true; phase.Failure = Safe(error.GetType().Name + ": " + error.Message);
                evidence.Failure = phase.Failure;
                throw;
            }
            finally
            {
                phase.Seconds = timing.Elapsed.TotalSeconds;
                evidence.Usage = operation.Stats; save();
            }
        }
        public Task<PlayAdvice> AdviseAsync(PlayObservation o, CancellationToken ct) =>
            Invoke("navigation", () => inner.AdviseAsync(o, ct), result => result.Actions.Count);
        public Task<List<VariableName>> NameVariablesAsync(VariableQuestion q, CancellationToken ct) =>
            Invoke("variable-names", () => inner.NameVariablesAsync(q, ct), result => result.Count);
        public Task<List<LabExperiment>> PlanExperimentsAsync(PreparationExperimentQuestion q, CancellationToken ct) =>
            Invoke("input-experiments", () => inner.PlanExperimentsAsync(q, ct), result => result.Count);
    }

    private static string Safe(string message)
    {
        string safe = Regex.Replace(message, @"(?i)(authorization|password|api[_ -]?key|access[_ -]?token)(\s*[:=]\s*)\S+", "$1$2[omitido]");
        safe = Regex.Replace(safe, @"\b(?:sk-|sess-)[A-Za-z0-9_-]+", "[omitido]");
        return safe.Length <= 1400 ? safe : safe[..1400] + " [detalle adicional omitido]";
    }

    private static string EmuRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "RetroEmu.slnx"))) return dir.FullName;
        throw new DirectoryNotFoundException("No se encontró la raíz emu para guardar evidencia local.");
    }

    [Test, Explicit("Consume como máximo dos pedidos de la CLI configurada, solo con autorización vigente."), Category("RealAi")]
    public async Task OneConfiguredSubscriptionCliCanResearchOnlyTheSyntheticNromWithinTheAuthorizedBudget()
    {
        if (Environment.GetEnvironmentVariable("RETROEMU_REAL_AI_QA") != "1")
            Assert.Ignore("La campaña requiere RETROEMU_REAL_AI_QA=1 y autorización explícita.");
        string output = Path.Combine(EmuRoot(), "out", "mantenimiento", "consolidacion");
        Directory.CreateDirectory(output);
        string reportPath = Path.Combine(output, "T127-real-ai-smoke.json");
        string oncePath = Path.Combine(output, "T127-real-ai-smoke.once");
        if (File.Exists(oncePath)) Assert.Ignore("La única campaña T127 ya fue iniciada; no se repite.");
        using (var once = new FileStream(oncePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            JsonSerializer.Serialize(once, new { campaign = "T127", startedUtc = DateTime.UtcNow });
        var evidence = new Evidence();
        void Save() => File.WriteAllText(reportPath, JsonSerializer.Serialize(evidence, GameProfile.Json));
        Save();
        string fixtureFolder = Path.Combine(Path.GetTempPath(), "retro-real-ai-T127-" + Guid.NewGuid().ToString("N"));
        AiOperation? operation = null;
        try
        {
            string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RetroEmu", "ia.json");
            if (!File.Exists(settingsPath))
            { evidence.Status = "not-configured"; evidence.Failure = "No hay una configuración guardada de IA; no se ejecutó un proveedor."; throw new NotRun(); }
            var settings = AiSettings.Load();
            evidence.Provider = settings.Provider;
            if (settings.Provider is not ("sub-codex" or "sub-claude" or "sub-google"))
            { evidence.Status = "provider-not-authorized"; evidence.Failure = "La selección guardada no es una CLI de suscripción autorizada para esta campaña."; throw new NotRun(); }
            var tool = CliTools.ById(settings.Provider)!;
            if (CliTools.Find(tool, settings.CliPaths.GetValueOrDefault(settings.Provider)) == null)
            { evidence.Status = "cli-unavailable"; evidence.Failure = "No se encontró la CLI ya configurada; no se instaló ni cambió ninguna herramienta."; throw new NotRun(); }
            long configuredLimit = Math.Clamp(settings.TextLimitThousands, 0, 10000) * 1000L;
            evidence.MaxTextCharacters = configuredLimit > 0 ? Math.Min(configuredLimit, 60000) : 60000;
            var provider = settings.CreateProvider(TimeSpan.FromMinutes(3))
                ?? throw new InvalidOperationException("La configuración autorizada no creó un proveedor.");
            operation = new AiOperation(provider, 2, evidence.MaxTextCharacters);
            var advisor = new StopAfterFailureAdvisor(new AiPlayAdvisor(operation), operation, evidence, Save);
            Directory.CreateDirectory(fixtureFolder);
            string original = Path.Combine(fixtureFolder, "synthetic-nrom.nes");
            File.WriteAllBytes(original, PreparationNesFixture.Rom());
            evidence.FixtureSha256 = AutonomousResearch.FileHash(original);
            var profile = new GameProfile { Id = "own-real-ai-T127", Name = "Programa NROM sintético", Machine = "nes",
                Match = new() { Sha256 = [evidence.FixtureSha256] } };
            var options = new AutonomousResearchOptions(TargetVariables: 2, Minutes: 3, AiCalls: 2, MaxPasses: 1, LabFrames: 120,
                Goal: "Investigar movimiento horizontal, salto y contadores del programa observado. Proponer entradas acotadas y distinguir candidatos, efectos de escritura y significados todavía no comprobados.");
            evidence.Status = "running"; Save();
            using var totalDeadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var work = await new AutonomousResearch().RunAsync(original, profile, fixtureFolder, options, advisor,
                progress: p =>
                {
                    evidence.Progress.Add(p with { Note = Safe(p.Note) });
                    if (evidence.Progress.Count > 96) evidence.Progress.RemoveAt(0);
                    evidence.Usage = operation.Stats; Save();
                }, ct: totalDeadline.Token);
            evidence.EngineStatus = work.Status; evidence.StopReason = Safe(work.StopReason);
            evidence.CompletedPasses = work.CompletedPasses; evidence.Candidates = work.Candidates;
            evidence.VerifiedWriteEffects = work.VerifiedWrites;
            evidence.Observations = work.Evidence.Findings.Take(12).Select(f =>
                $"${f.Address} {f.Kind}, {f.Encoding}/{f.Bytes}, estado {f.Status}: " +
                string.Join("; ", f.Verification?.Observations.Take(2) ?? f.Evidence.Take(2))).Select(Safe).ToList();
            evidence.Status = evidence.Phases.Any(p => p.Parsed)
                ? evidence.Phases.Any(p => p.Failure.Length > 0) ? "partial-ai-response" : "parsed-ai-response"
                : evidence.Phases.Count > 0 ? "no-valid-ai-response" : "local-only-no-ai-call";
            if (work.Status is "error" or "limit" or "paused") evidence.Status += "/" + work.Status;
            Assert.That(AutonomousResearch.FileHash(original), Is.EqualTo(evidence.FixtureSha256));
        }
        catch (NotRun) { }
        catch (Exception error)
        { evidence.Status = "campaign-error"; evidence.Failure = Safe(error.GetType().Name + ": " + error.Message); }
        finally
        {
            evidence.Usage = operation?.Stats; evidence.CompletedUtc = DateTime.UtcNow; Save();
            string tempRoot = Path.GetFullPath(Path.GetTempPath());
            string target = Path.GetFullPath(fixtureFolder);
            if (!target.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("La carpeta temporal no quedó bajo el directorio previsto.");
            if (Directory.Exists(target)) Directory.Delete(target, true);
        }
        TestContext.Progress.WriteLine("Campaña T127: " + evidence.Status + "; evidencia local: " + reportPath);
        Assert.That(evidence.Usage?.Calls ?? 0, Is.LessThanOrEqualTo(2));
        Assert.That(evidence.Usage?.TextCharacters ?? 0, Is.LessThanOrEqualTo(evidence.MaxTextCharacters));
        if (!evidence.Phases.Any(p => p.Parsed) || evidence.Status == "campaign-error")
            Assert.Inconclusive("La campaña no produjo una respuesta de IA válida o encontró un error; el informe conserva el resultado sin reintentar.");
    }
}
