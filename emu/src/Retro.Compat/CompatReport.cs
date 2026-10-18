using System.Text;
using System.Text.Json;
using Retro.Machines;

namespace Retro.Compat;

/// <summary>All the results of a run, saved as JSON (evidence) and Markdown (for people).</summary>
public sealed class CompatReport
{
    public string Generated { get; set; } = "";
    public string Commit { get; set; } = "";
    public string GamesFolder { get; set; } = "";
    public string Note { get; set; } = "";
    public List<CompatResult> Results { get; set; } = new();

    public void SaveJson(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, CompatScript.Json) + "\n");
    }

    public static CompatReport LoadJson(string path) =>
        JsonSerializer.Deserialize<CompatReport>(File.ReadAllText(path), CompatScript.Json) ?? new CompatReport();

    /// <summary>Merges new results into an earlier report (same id = replaced).</summary>
    public void Merge(IEnumerable<CompatResult> fresh)
    {
        var byId = Results.ToDictionary(r => r.Id);
        foreach (var r in fresh)
            byId[r.Id] = r;
        Results = byId.Values.OrderBy(r => r.Machine).ThenBy(r => r.Game).ThenBy(r => r.Id).ToList();
    }

    private static string Mark(LevelResult l) => l.Status switch
    {
        "ok" => "sí",
        "partial" => "parcial",
        "fail" => "**no**",
        _ => "·",
    };

    private static string MachineName(string id) => MachineCatalog.All.FirstOrDefault(m => m.Id == id)?.Name ?? id;

    public string ToMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Matriz de compatibilidad");
        sb.AppendLine();
        sb.AppendLine($"Generada el {Generated} (commit {Commit}) con `retro-cli compat` sobre `{GamesFolder}`. " +
                      "Los datos completos (hash, procedimiento, evidencia de cada nivel) están en `emu/compat/resultados.json`; " +
                      "los guiones, en `emu/compat/juegos/`. No editar a mano: se regenera.");
        sb.AppendLine();
        sb.AppendLine("Niveles, en orden: **formato** reconocido → medio **cargado** → **imagen** (pantalla con contenido y sin signos de fallo) → " +
                      "llega al **título** (comprobación del guion) → **responde** a los controles (una entrada cambia la imagen respecto de la misma " +
                      "corrida sin entradas) → **jugable** (comprobaciones de partida del guion) → listo para **fusionar** (perfil con direcciones verificadas). " +
                      "`sí` = comprobado, **no** = falló, `parcial` = a medias, `·` = no probado. Son pruebas cortas y reproducibles, no partidas completas.");
        if (!string.IsNullOrEmpty(Note))
        {
            sb.AppendLine();
            sb.AppendLine(Note);
        }
        foreach (var group in Results.GroupBy(r => r.Machine).OrderBy(g => MachineLauncher.IsImplemented(g.Key) ? 0 : 1).ThenBy(g => g.Key))
        {
            sb.AppendLine();
            int highest(CompatResult r) => Array.IndexOf(CompatLevels.All, r.Highest);
            int titles = group.Count(r => highest(r) >= 3 || r.Levels["title"].Status == "ok");
            sb.AppendLine($"## {MachineName(group.Key)} ({group.Count()} archivos{(MachineLauncher.IsImplemented(group.Key) ? $", {group.Count(r => r.Levels["load"].Status == "ok")} cargan, {group.Count(r => r.Levels["controls"].Status == "ok")} responden a controles" : ", máquina no implementada")})");
            sb.AppendLine();
            int checkedTwice = group.Count(r => r.Warnings.Any(w => w.StartsWith("determinista") || w.StartsWith("no determinista")));
            if (checkedTwice > 0)
            {
                int same = group.Count(r => r.Warnings.Any(w => w.StartsWith("determinista")));
                sb.AppendLine($"Determinismo: {same} de {checkedTwice} corridas repetidas dieron exactamente la misma imagen final.");
                sb.AppendLine();
            }
            sb.AppendLine("| Juego | Archivo | SHA-256 | Formato | Carga | Imagen | Título | Responde | Jugable | Fusión | Observaciones |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (var r in group)
            {
                var notes = new List<string>();
                notes.AddRange(r.Problems);
                notes.AddRange(r.GameQuirks.Select(q => "del juego: " + q));
                notes.AddRange(r.Warnings.Where(w => !w.StartsWith("determinista")));
                if (r.Levels["controls"].Status == "ok")
                    notes.Add(r.Levels["controls"].Detail);
                if (!string.IsNullOrEmpty(r.Notes))
                    notes.Add(r.Notes);
                string file = r.Member != null && !r.File.EndsWith(Path.GetFileName(r.Member), StringComparison.OrdinalIgnoreCase)
                    ? $"{r.File} → {Path.GetFileName(r.Member)}" : r.File;
                sb.AppendLine($"| {r.Game}{(r.Curated ? "" : " ⁽ᵃ⁾")} | {Esc(file)} | {(r.Sha256 ?? "-")[..Math.Min(12, (r.Sha256 ?? "-").Length)]} | " +
                              string.Join(" | ", CompatLevels.All.Select(l => Mark(r.Levels[l]))) + $" | {Esc(string.Join("; ", notes))} |");
            }
        }
        sb.AppendLine();
        sb.AppendLine("⁽ᵃ⁾ Guion generado automáticamente (sin pasos ni comprobaciones escritos a mano): solo puede llegar a «imagen» y «responde».");
        return sb.ToString();
    }

    private static string Esc(string s) => s.Replace("|", "\\|").Replace("\n", " ");
}
