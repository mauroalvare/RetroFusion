using System.Text.Json;
using System.Text.RegularExpressions;
using Retro.Compat;
using Retro.Fusion;

namespace Retro.App;

public sealed record LegacyResearchIdea(string Title,string Goal,IReadOnlyList<string> Profiles);

/// <summary>Recover goals from older labs as unverified, editable proposals; never infer source slots or runnable code.</summary>
public static class LegacyResearchImport
{
    public static IReadOnlyList<LegacyResearchIdea> Read(IEnumerable<string> availableProfiles,string? folder=null)
    {
        folder??=Path.Combine(ProfileLibrary.UserFolder,"lab");
        if (!Directory.Exists(folder)) return [];
        var ideas=new Dictionary<string,(string Title,List<string> Goals,List<string> Profiles)>(StringComparer.OrdinalIgnoreCase);
        foreach (string id in availableProfiles.Distinct().Take(200))
        {
            if (string.IsNullOrWhiteSpace(id) || id.IndexOfAny(Path.GetInvalidFileNameChars())>=0 || id is "." or ".." || id.Contains('/') || id.Contains('\\')) continue;
            var file=new FileInfo(Path.Combine(folder,id+UiText.Get(".json")));
            if (!file.Exists || file.Length>1024*1024 || file.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
            try
            {
                var lab=JsonSerializer.Deserialize<LabReport>(File.ReadAllText(file.FullName),GameProfile.Json);
                var seenTitles=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach(string note in lab?.Notes?.Where(n=>n!=null).TakeLast(48).Reverse()??[])
                {
                    if (note.Length>16_000 || !note.StartsWith("Revisar qué requisitos",StringComparison.Ordinal)) continue;
                    var title=Regex.Match(note,@"(?m)^Idea:\s*(.+)$").Groups[1].Value.Trim();
                    int start=note.IndexOf("Datos que faltan:",StringComparison.Ordinal);
                    if (title.Length is 0 or >160 || start<0 || !seenTitles.Add(title)) continue;
                    string goal=note[(start+"Datos que faltan:".Length)..].Trim();
                    if (!ideas.TryGetValue(title,out var idea)) idea=(title,[],[]);
                    if (!idea.Profiles.Contains(id)) idea.Profiles.Add(id);
                    if (!idea.Goals.Contains(goal)) idea.Goals.Add(goal);
                    ideas[title]=idea;
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
        }
        return ideas.Values.Where(i=>i.Profiles.Count is >=2 and <=3).Take(8).Select(i=>
        {
            string goals=string.Join("\n\nOtro registro de la misma idea (revisar correspondencia):\n",i.Goals.Take(3));
            if (goals.Length>7000) goals=goals[..6900]+UiText.Get("\n[Texto recortado: revisá también los registros de laboratorio originales.]");
            return new LegacyResearchIdea(i.Title,goals,i.Profiles);
        }).ToList();
    }
}
