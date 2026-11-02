using System.Security.Cryptography;
using System.Text.Json;
using Retro.Core;
using Retro.Fusion;

namespace Retro.Compat;

public sealed record AutonomousResearchOptions(int TargetVariables=12,int Minutes=5,int AiCalls=12,int MaxPasses=8,int LabFrames=900,string Goal="")
{
    public void Validate()
    {
        if(TargetVariables is <1 or >64 || Minutes is <1 or >60 || AiCalls is <0 or >100 || MaxPasses is <1 or >24 || LabFrames is <60 or >9000 || Goal is not {Length:<=4000})
            throw new ArgumentOutOfRangeException(nameof(AutonomousResearchOptions),"Presupuesto de investigación fuera de los límites.");
    }
}

public sealed record AutonomousResearchProgress(string Stage,double ElapsedSeconds,int Pass,int Candidates,int VerifiedWrites,int AiCalls,string Note);

/// <summary>Private evidence; a verified write effect does not certify a variable's proposed meaning.</summary>
public sealed class AutonomousResearchWork
{
    public int Format {get;set;}=1;
    public string Id {get;set;}=Guid.NewGuid().ToString("N");
    public string FileSha256 {get;set;}="";
    public string EditionSha256 {get;set;}="";
    public string FirmwareSha256 {get;set;}="";
    public string ProfileStamp {get;set;}="";
    public string Engine {get;set;}="";
    public string Game {get;set;}="";
    public string Status {get;set;}="ready";
    public string StopReason {get;set;}="";
    public double ElapsedSeconds {get;set;}
    public int CompletedPasses {get;set;}
    public int AiCallsUsed {get;set;}
    public AutonomousResearchOptions Options {get;set;}=new();
    public GameProfile? Profile {get;set;}
    public PreparationContinuation? Continuation {get;set;}
    public LabReport Evidence {get;set;}=new();
    public List<PreparationExperimentResult> Experiments {get;set;}=[];
    public List<string> Checked {get;set;}=[];
    public int Candidates=>Evidence.Findings.Count;
    public int VerifiedWrites=>Evidence.Findings.Count(f=>f.Status=="verified");
}

/// <summary>Research on disposable machines, never on the player's current machine or source files.</summary>
public sealed class AutonomousResearch
{
    private const string HistoricalEvidence = "Evidencia histórica: cambió el motor o el perfil. Es necesario revalidar este efecto antes de contarlo como comprobado en el contexto actual.";
    public static string EngineFingerprint => MachineFactory.RuntimeFingerprint + ";" + typeof(GamePreparer).Module.ModuleVersionId;
    public Func<string,PrepareOptions,Action<string>?,CancellationToken,Task<PrepareResult>> Prepare {get;init;}=GamePreparer.PrepareAsync;
    public TimeProvider Clock {get;init;}=TimeProvider.System;
    public static string FileHash(string path) {using var file=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(file));}
    private static string Stamp(GameProfile profile)=>Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(profile.ToJson())));

    public static string ResolveOriginal(string source)
    {
        string path=source;
        for(int part=0;part<12;part++)
        {
            if(File.Exists(path))return Path.GetFullPath(path);
            int split=path.LastIndexOf('!');if(split<0)break;path=path[..split];
        }
        throw new FileNotFoundException("No se encontró el archivo original del análisis. Volvé a abrirlo desde Biblioteca.");
    }

    public async Task<AutonomousResearchWork> RunAsync(string path,GameProfile profile,string firmware,AutonomousResearchOptions options,
        IPlayAdvisor? advisor=null,AutonomousResearchWork? previous=null,Action<AutonomousResearchProgress>? progress=null,
        Action<AutonomousResearchWork>? checkpoint=null,CancellationToken ct=default)
    {
        options.Validate();path=ResolveOriginal(path);
        if(!GamePreparer.CanPrepare(profile.Machine))throw new NotSupportedException("Esta máquina todavía no dispone de laboratorio automático.");
        string hash=FileHash(path),rom=MachineFactory.FirmwareFingerprint(profile.Machine,firmware),stamp=Stamp(profile);
        // The edition inside an archive has a different identity from its container.
        var selected=GamePreparer.Choose(MediaArchive.Open(path),profile);
        string edition=selected.media!=null?GamePreparer.Sha256Of(selected.media):hash;
        string engine=EngineFingerprint;
        if(previous!=null && (previous.FileSha256!=hash || previous.FirmwareSha256!=rom))throw new InvalidDataException("Cambió el original o el firmware. Los datos anteriores se conservan; iniciá un análisis nuevo.");
        if(previous!=null && !MatchesEdition(previous,[edition]))throw new InvalidDataException("El análisis guardado corresponde a otra edición dentro del archivo. Iniciá otro análisis para la edición abierta.");
        var work=previous??new AutonomousResearchWork {FileSha256=hash,FirmwareSha256=rom,ProfileStamp=stamp,Engine=engine,Game=profile.Name,Profile=GameProfile.Parse(profile.ToJson())};
        work.EditionSha256=edition;
        InvalidateChangedContext(work,profile);
        work.Options=options;work.Engine=engine;work.ProfileStamp=stamp;work.Status="running";work.StopReason="";
        long started=Clock.GetTimestamp();double priorElapsed=work.ElapsedSeconds;int priorCalls=work.AiCallsUsed;
        var counted=advisor==null || options.AiCalls==0?null:new BoundedAdvisor(advisor,options.AiCalls);
        using var deadline=new CancellationTokenSource(TimeSpan.FromMinutes(options.Minutes),Clock);
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct,deadline.Token);
        string stage="Inicio";int pass=0;
        void Publish(string note,bool save=false)
        {
            work.ElapsedSeconds=priorElapsed+Clock.GetElapsedTime(started).TotalSeconds;work.AiCallsUsed=priorCalls+(counted?.Calls??0);
            progress?.Invoke(new(stage,work.ElapsedSeconds,work.CompletedPasses,work.Candidates,work.VerifiedWrites,work.AiCallsUsed,note));
            if(save)checkpoint?.Invoke(work);
        }
        void CheckIdentity()
        {
            if(FileHash(path)!=hash || MachineFactory.FirmwareFingerprint(profile.Machine,firmware)!=rom)
                throw new InvalidDataException("Cambió el original o el firmware durante el análisis. No se mezclaron ediciones.");
        }
        void CheckMetadata(string? machine,string? sha,string source)
        {
            if(!string.IsNullOrWhiteSpace(machine) && machine!=profile.Machine ||
                !string.IsNullOrWhiteSpace(sha) && !sha.Equals(edition,StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(source+Retro.Core.UiText.Get(" corresponde a otra máquina o edición. No se incorporó al análisis."));
        }
        void CheckLab(LabReport lab)=>CheckMetadata(lab.Machine,lab.Sha256,"El informe del laboratorio");
        try
        {
            Publish("Guardando el objetivo antes de investigar.",true);
            int stalled=0;
            for(pass=1;pass<=options.MaxPasses;pass++)
            {
                linked.Token.ThrowIfCancellationRequested();CheckIdentity();
                string before=EvidenceStamp(work.Evidence);stage="Arranque y experimentos";
                var result=await Prepare(path,new PrepareOptions {PreviousProfile=work.Profile,Resume=work.Continuation,
                    FirmwareDirectory=firmware,Advisor=counted,Save=false,Goal=string.IsNullOrWhiteSpace(options.Goal)?"Investigar movimiento, físicos, enemigos, reloj, dificultad y estados con evidencia; no inferir propósitos por el nombre.":options.Goal,
                    ContinueResearch=true,InvestigationPass=work.CompletedPasses+1,PreviousExperiments=work.Experiments,
                    TrialRounds=3,MaxRounds=12,LabFrames=options.LabFrames,
                    EvidenceUpdated=lab=> {CheckIdentity();CheckLab(lab);stage="Contraste de hipótesis";Merge(work.Evidence,lab);Publish("Candidatos observados; la escritura y el significado se distinguen.",true);}},
                    note=> {stage=Stage(note);Publish(note);},linked.Token).ConfigureAwait(false);
                linked.Token.ThrowIfCancellationRequested();CheckIdentity();
                CheckMetadata(result.Machine,result.Sha256,"El resultado de preparación");
                if(result.Lab!=null)CheckLab(result.Lab);
                if(result.Profile is { } returned)
                {
                    CheckMetadata(returned.Machine,null,"El perfil preparado");
                    if(returned.Match.Sha256.Count>0 && !returned.Match.Sha256.Contains(edition,StringComparer.OrdinalIgnoreCase))
                        throw new InvalidDataException("El perfil preparado corresponde a otra edición. No se incorporó al análisis.");
                }
                if(result.Continuation is { } continuation)CheckMetadata(continuation.Machine,continuation.Sha256,"La continuación preparada");
                if(result.Lab!=null)Merge(work.Evidence,result.Lab);
                work.Experiments=work.Experiments.Concat(result.Experiments).TakeLast(96).ToList();
                work.Checked=work.Checked.Concat(result.CheckedData).Distinct().TakeLast(48).ToList();
                if(result.Profile!=null)work.Profile=GameProfile.Parse(result.Profile.ToJson());
                work.Continuation=result.Continuation;work.CompletedPasses++;
                Publish("Ronda guardada.",true);
                if(!result.Ok) {work.Status="error";work.StopReason=result.Problem??"No se confirmó un arranque reproducible.";break;}
                if(work.VerifiedWrites>=options.TargetVariables) {work.StopReason="Se alcanzó el objetivo de efectos de escritura comprobados. Sus nombres pueden seguir siendo hipótesis.";break;}
                stalled=before==EvidenceStamp(work.Evidence)?stalled+1:0;
                if(stalled>=2) {work.StopReason="Dos rondas sin evidencia nueva. Se conserva lo obtenido; ampliar el presupuesto no garantiza más datos.";break;}
                if(counted!=null && counted.Calls>=options.AiCalls) {work.Status="limit";work.StopReason="Se alcanzó el presupuesto de pedidos de IA. Los datos locales se conservaron.";break;}
            }
            if(work.Status=="running")work.Status="complete";
            if(work.StopReason.Length==0)work.StopReason="Se completaron las rondas configuradas. El objetivo de cantidad no garantiza identificaciones suficientes.";
        }
        catch(OperationCanceledException) when(linked.IsCancellationRequested)
        {
            work.Status=ct.IsCancellationRequested?"paused":"limit";
            work.StopReason=ct.IsCancellationRequested?"Análisis detenido. Se conservan las rondas y la evidencia completadas; la prueba interrumpida puede repetirse.":"Se alcanzó el tiempo configurado. El progreso queda guardado para continuar.";
        }
        catch(Exception error) when(error is not OutOfMemoryException)
        {work.Status="error";work.StopReason=Retro.Core.UiText.Get("No se completó el análisis: ")+error.Message;}
        stage="Guardado";Publish(work.StopReason,true);return work;
    }

    public static bool MatchesEdition(AutonomousResearchWork work,IEnumerable<string> hashes)
    {
        var current=hashes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Older records may identify the selected member through their evidence or
        // continuation. The container hash is only an edition hash for a loose file.
        string identity=work.EditionSha256.Length>0?work.EditionSha256:
            !string.IsNullOrEmpty(work.Evidence.Sha256)?work.Evidence.Sha256:
            work.Continuation?.Sha256??"";
        if(identity.Length>0)return current.Contains(identity);
        if(work.Profile?.Match.Sha256 is {Count:>0} editions)return editions.Any(current.Contains);
        return current.Contains(work.FileSha256);
    }

    public static bool InvalidateChangedContext(AutonomousResearchWork work,GameProfile profile)
    {
        if(work.Engine==EngineFingerprint && work.ProfileStamp==Stamp(profile))return false;
        work.Continuation=null;work.Profile=GameProfile.Parse(profile.ToJson());
        foreach(var finding in work.Evidence.Findings.Where(f=>f.Status=="verified"))
        {
            finding.Status="inconclusive";
            finding.Evidence??=[];
            if(!finding.Evidence.Contains(HistoricalEvidence))finding.Evidence.Insert(0,HistoricalEvidence);
        }
        return true;
    }

    private static string Stage(string note)=>note.Contains("verific",StringComparison.OrdinalIgnoreCase)?"Verificando escrituras":
        note.Contains("nombr",StringComparison.OrdinalIgnoreCase)?"Hipótesis de nombres":note.Contains("Laboratorio",StringComparison.OrdinalIgnoreCase)?"Observando y comparando RAM":"Ejecutando entradas";
    private static string EvidenceStamp(LabReport report)=>JsonSerializer.Serialize(report.Findings.Select(f=>new {f.Address,f.Kind,f.Encoding,f.Bytes,f.Status}).OrderBy(f=>f.Address).ThenBy(f=>f.Kind));
    private static void Merge(LabReport target,LabReport source)
    {
        target.Game=source.Game;target.Machine=source.Machine;target.Sha256=source.Sha256;target.Method=source.Method;
        target.Notes=target.Notes.Concat(source.Notes).Distinct().TakeLast(48).ToList();
        foreach(var control in source.CheckedControls.Take(32))
            if(target.CheckedControls.Count<32 || target.CheckedControls.ContainsKey(control.Key))target.CheckedControls[control.Key]=control.Value;
        foreach(var finding in source.Findings.Take(256))
        {
            string key=Retro.Core.UiText.Format($"{finding.Address}/{finding.Kind}/{finding.Encoding}/{finding.Bytes}");
            int at=target.Findings.FindIndex(f=>Retro.Core.UiText.Format($"{f.Address}/{f.Kind}/{f.Encoding}/{f.Bytes}")==key);
            LabFinding Clone()=>JsonSerializer.Deserialize<LabFinding>(JsonSerializer.Serialize(finding,GameProfile.Json),GameProfile.Json)!;
            if(at>=0)
            {
                var prior=target.Findings[at];
                bool refutation=finding.Status=="refuted" && finding.Verification?.Result=="refuted";
                bool untestedHistorical=prior.Evidence?.Contains(HistoricalEvidence)==true && finding.Verification==null && finding.Status!="verified";
                if(!untestedHistorical && (prior.Status!="verified" || finding.Status=="verified" || refutation))target.Findings[at]=Clone();
            }
            else if(target.Findings.Count<256)target.Findings.Add(Clone());
        }
    }

    private sealed class BoundedAdvisor(IPlayAdvisor inner,int limit):IPlayAdvisor,IVariableNamer,IPreparationExperimentAdvisor
    {
        private int _calls;
        public string Name=>inner.Name;public int Calls=>Volatile.Read(ref _calls);
        private void Reserve(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            while(true) {int used=Calls;if(used>=limit)throw new InvalidOperationException("Límite de pedidos de investigación alcanzado.");if(Interlocked.CompareExchange(ref _calls,used+1,used)==used)return;}
        }
        public Task<PlayAdvice> AdviseAsync(PlayObservation o,CancellationToken ct){Reserve(ct);return inner.AdviseAsync(o,ct);}
        public Task<List<VariableName>> NameVariablesAsync(VariableQuestion q,CancellationToken ct)
        {if(inner is not IVariableNamer n)return Task.FromResult(new List<VariableName>());Reserve(ct);return n.NameVariablesAsync(q,ct);}
        public Task<List<LabExperiment>> PlanExperimentsAsync(PreparationExperimentQuestion q,CancellationToken ct)
        {if(inner is not IPreparationExperimentAdvisor p)return Task.FromResult(new List<LabExperiment>());Reserve(ct);return p.PlanExperimentsAsync(q,ct);}
    }
}
