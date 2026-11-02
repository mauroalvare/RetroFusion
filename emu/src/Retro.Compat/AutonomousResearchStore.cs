using System.Text.Json;
using Retro.Fusion;

namespace Retro.Compat;

/// <summary>Private, bounded checkpoints; no arbitrary source path is trusted when continuing.</summary>
public sealed class AutonomousResearchStore(string folder)
{
    public string Folder {get;}=Path.GetFullPath(folder);
    public const int MaxBytes=4*1024*1024;
    public string Problem {get;private set;}="";
    private void CheckFolder()
    {
        for(var dir=new DirectoryInfo(Folder);dir!=null;dir=dir.Parent)
            if(dir.Exists && dir.Attributes.HasFlag(FileAttributes.ReparsePoint))throw new IOException("La carpeta del análisis contiene un enlace.");
    }
    private string PathFor(string id)
    {
        if(id is not {Length:32} || id.Any(c=>!char.IsAsciiHexDigit(c)))throw new InvalidDataException("Identificador de análisis inválido.");
        return Path.Combine(Folder,id+Retro.Core.UiText.Get(".json"));
    }
    private FileStream Lease()
    {
        string path=Path.Combine(Folder,".store.lock");
        if(File.Exists(path)&&File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))throw new IOException("El bloqueo del análisis es un enlace.");
        return new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
    }
    private static void Validate(AutonomousResearchWork work)
    {
        if(work.Format!=1 || work.FileSha256 is not {Length:64} || !work.FileSha256.All(char.IsAsciiHexDigit) ||
            work.EditionSha256==null || work.EditionSha256.Length>0 && (work.EditionSha256.Length!=64 || !work.EditionSha256.All(char.IsAsciiHexDigit)) || work.Options==null || work.Evidence?.Findings==null ||
            work.Evidence.Notes==null || work.Evidence.Notes.Count>48 || work.Evidence.CheckedControls==null || work.Evidence.CheckedControls.Count>32 ||
            work.Evidence.Findings.Count>256 || work.Experiments==null || work.Experiments.Count>96 || work.Checked==null || work.Checked.Count>48 ||
            work.Continuation is {State.Length:>1024*1024} || work.Status==null || work.StopReason==null || work.Engine==null || work.ProfileStamp==null || work.Game==null ||
            !double.IsFinite(work.ElapsedSeconds) || work.ElapsedSeconds<0 || work.CompletedPasses<0 || work.AiCallsUsed<0 ||
            work.Evidence.Findings.Any(f=>f==null || f.Address==null || f.Status==null || f.Kind==null || f.Encoding==null))
            throw new InvalidDataException("Análisis guardado inválido o fuera de los límites.");
        work.Options.Validate();
    }
    public void Save(AutonomousResearchWork work)
    {
        Validate(work);CheckFolder();string path=PathFor(work.Id);Directory.CreateDirectory(Folder);
        // One lease also protects the shared twelve-record quota across different jobs.
        using var lease=Lease();
        if(File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))throw new IOException("El análisis es un enlace.");
        byte[] data=JsonSerializer.SerializeToUtf8Bytes(work,GameProfile.Json);
        if(data.Length>MaxBytes)throw new IOException("El análisis supera4MB. El checkpoint anterior se conserva.");
        if(!File.Exists(path) && Directory.EnumerateFiles(Folder,"*.json").Count()>=12)throw new IOException("Hay doce análisis guardados. Descartá uno que ya no necesites.");
        string temp=path+"."+Guid.NewGuid().ToString("N")+Retro.Core.UiText.Get(".tmp");
        try
        {
            using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(data);stream.Flush(true);}
            File.Move(temp,path,true);
        }
        finally {if(File.Exists(temp))File.Delete(temp);}
    }
    public IReadOnlyList<AutonomousResearchWork> Read()
    {
        Problem="";CheckFolder();if(!Directory.Exists(Folder))return[];
        var works=new List<AutonomousResearchWork>();
        foreach(var file in new DirectoryInfo(Folder).EnumerateFiles("*.json").OrderByDescending(f=>f.LastWriteTimeUtc).Take(12))
        {
            try
            {
            if(file.Length>MaxBytes || file.Attributes.HasFlag(FileAttributes.ReparsePoint))throw new InvalidDataException("Un análisis guardado es demasiado grande o es un enlace. No se borró.");
            var work=JsonSerializer.Deserialize<AutonomousResearchWork>(File.ReadAllText(file.FullName),GameProfile.Json)??throw new InvalidDataException("Análisis vacío.");
            Validate(work);if(Path.GetFileName(PathFor(work.Id))!=file.Name)throw new InvalidDataException("El identificador del archivo no coincide.");works.Add(work);
            }
            catch(Exception error) when(error is IOException or InvalidDataException or JsonException or ArgumentException or NullReferenceException)
            {Problem=Retro.Core.UiText.Get("No se pudo leer un análisis; se conservó el archivo ")+file.Name+". "+error.Message;}
        }
        return works;
    }
    public void Remove(string id)
    {
        CheckFolder();string path=PathFor(id);
        if(File.Exists(path)&&File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))throw new IOException("El análisis es un enlace.");
        if(!Directory.Exists(Folder))return;
        using(var lease=Lease())File.Delete(path);
    }
}
