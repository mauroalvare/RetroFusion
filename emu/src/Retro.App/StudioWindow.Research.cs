using System.Security.Cryptography;
using ImGuiNET;
using Retro.Compat;
using Retro.Fusion;
using Retro.Studio;

namespace Retro.App;

public sealed partial class StudioWindow
{
    private readonly FusionResearchStore _researchStore;
    private readonly Action<string>? _playOriginal, _analyzeOriginal;
    private FusionResearchWork? _researchWork, _resumeResearch;
    private IReadOnlyList<FusionResearchWork> _pendingResearch = [];
    private bool _showResearch;
    private string? _researchReadProblem;
    private IReadOnlyList<LegacyResearchIdea>? _legacyIdeas;
    private string? _observationFor;
    private string _humanObservation="";
    private int _quickOriginal;

    internal bool OpenResearchOriginal(string slot,bool analyze=false)
    {
        var original=_researchWork?.Originals.FirstOrDefault(o=>o.Slot==slot);
        if (original==null || !CanFindOriginal(original,out _,out string path)) return false;
        var action=analyze?_analyzeOriginal:_playOriginal;
        if (action==null) return false;
        action(path); return true;
    }

    public void ShowResearch(string id)
    {
        Open(); _savedPage=false;
        var work=_pendingResearch.FirstOrDefault(w=>w.Id==id);
        if (work==null) { Problem("No se encontró la investigación guardada."); return; }
        _inspiredMode=work.Inspired; _researchWork=work; _showResearch=true;
    }

    private void PauseResearch()
    {
        if (_researchWork==null) return;
        _researchWork.Status="paused"; _showResearch=true;
        try { _researchStore.Save(_researchWork); RefreshWork(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { _progress=UiText.Get("El último checkpoint se conserva; no se pudo actualizar la pausa: ")+error.Message; }
    }

    private void DrawLegacyRecovery()
    {
        if (Busy) return;
        if (ImGui.Button(UiText.Label("Buscar investigaciones de versiones anteriores"))) _legacyIdeas=LegacyResearchImport.Read(_games.Select(g=>g.Profile.Id));
        if (_legacyIdeas==null) return;
        if (_legacyIdeas.Count==0) { UiTheme.Hint("No encontré objetivos comunes de laboratorio para los originales de esta carpeta."); return; }
        foreach(var idea in _legacyIdeas)
        {
            ImGui.PushID(idea.Goal); UiTheme.BeginCard("legacy-idea");
            ImGui.TextColored(UiTheme.Accent,idea.Title);
            UiTheme.Hint(UiText.Get("Objetivo recuperado de ")+string.Join(" + ",idea.Profiles.Select(id=>_games.First(g=>g.Profile.Id==id).Profile.Name)));
            ImGui.TextWrapped(UiText.Get("El registro anterior puede estar recortado y no conserva el orden de los originales ni la base. Revisalos antes de preparar la propuesta. No se recuperó un programa jugable."));
            if (ImGui.TreeNode(UiText.Label("Leer texto recuperado"))) { ImGui.TextWrapped(UiText.Get(idea.Goal)); ImGui.TreePop(); }
            if (ImGui.Button(UiText.Label("Revisar esta propuesta")))
            {
                _chosen.Clear(); _chosen.AddRange(idea.Profiles); _baseProfile=null;
                _wish=UiText.Get("Propuesta recuperada para revisar. Las letras de ranura del texto antiguo no identifican por sí solas a los originales actuales. Respetar la base que elija ahora el jugador.\n")+idea.Goal;
                _concepts=null; _showResearch=false; _savedPage=false; _focusWork=true;
            }
            UiTheme.EndCard(); ImGui.PopID();
        }
    }

    private static string FileIdentity(string path)
    { using var file=File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)); }

    private bool StartResearchCheckpoint(GameConcept concept, IReadOnlyList<RecipeGame> slots, IReadOnlyList<FusionPreparationTarget> targets,
        FusionResearchWork? resumed)
    {
        try
        {
            _researchWork = resumed ?? new() { Concept=concept, Inspired=_inspiredMode, Slots=slots.ToList(),
                Originals=slots.Select(slot=> { var target=targets.Single(t=>t.Source.Profile.Id==slot.Profile);
                    return new ResearchOriginal(slot.Slot,target.Path,FileIdentity(target.Path),slot.Profile,
                        MachineFactory.FirmwareFingerprint(target.Source.Profile.Machine,_romDir)); }).ToList() };
            _researchWork.Status="research"; _researchWork.Engine=FusionResearchWork.CurrentEngine;
            _researchStore.Save(_researchWork); _showResearch=true; return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        { Problem(UiText.Get("No se pudo guardar el punto de partida. La investigación no empezó: ")+error.Message); return false; }
    }

    private void CheckpointResearch(FusionPreparationResult result)
    {
        if (_researchWork==null) return;
        _researchWork.Concept=result.Concept; _researchWork.Slots=result.Slots.ToList();
        _researchWork.Preparations=result.Preparations.ToList(); _researchWork.Summary=result.Summary;
        _researchWork.Passes=result.ResearchPasses; _researchWork.StopReason=result.StopReason;
        _researchWork.Originals=_researchWork.Originals.Select(o=>o with { Profile=result.Slots.First(s=>s.Slot==o.Slot).Profile }).ToList();
        if (_cts?.IsCancellationRequested==true) _researchWork.Status="paused";
        _researchStore.Save(_researchWork);
    }

    private void ResearchBlocked(FusionPreparationResult result)
    {
        CheckpointResearch(result); _showResearch=true; _problem=_problemDetail=null;
        _build.Stop(false); _creationTiming?.Stop(false);
        _progress="Investigación guardada. Falta una comprobación antes de crear la fusión.";
        if (_researchWork!=null) { _researchWork.Status="blocked"; _researchStore.Save(_researchWork); }
        RefreshWork();
    }

    private bool CanFindOriginal(ResearchOriginal original, out GameSource source, out string path)
    {
        source=null!; path="";
        foreach (var candidate in _games)
            if (_originalPaths.TryGetValue(candidate.Profile.Id,out string? current) &&
                Path.GetFullPath(current).Equals(Path.GetFullPath(original.Path),StringComparison.OrdinalIgnoreCase))
            { source=candidate; path=current; return true; }
        return false;
    }

    private void ResumeResearch(FusionResearchWork work)
    {
        if (Busy) return;
        try
        {
            if (work.Concept.MissingData.Length==0 && work.ConstructionId is { } construction && StudioWorkStore.Pending().FirstOrDefault(w=>w.Id==construction) is { } draft)
            { _researchWork=work; _showResearch=false; _savedPage=false; ResumeWork(draft); return; }
            var mapped=new Dictionary<string,GameSource>();
            foreach (var original in work.Originals)
            {
                if (!CanFindOriginal(original,out var source,out string path)) throw new InvalidDataException("No se encontró un original de la investigación. Revisá la carpeta de juegos.");
                if (FileIdentity(path)!=original.Fingerprint || MachineFactory.FirmwareFingerprint(source.Profile.Machine,_romDir)!=original.Firmware)
                    throw new InvalidDataException("Cambió la edición o el firmware. El trabajo sigue guardado; requiere una investigación nueva.");
                mapped[original.Slot]=source;
            }
            bool sameEngine=work.Engine==FusionResearchWork.CurrentEngine;
            foreach (var preparation in work.Preparations)
            {
                var original=work.Originals.FirstOrDefault(o=>o.Profile==preparation.Profile?.Id);
                if (original==null) continue;
                var live=mapped[original.Slot].Profile;
                if (!sameEngine || preparation.Profile?.ToJson()!=live.ToJson()) preparation.Continuation=null;
                preparation.Profile=GameProfile.Parse(live.ToJson()); preparation.ProfilePath=null;
            }
            _inspiredMode=work.Inspired; _chosen.Clear();
            foreach (var slot in work.Slots) _chosen.Add(mapped[slot.Slot].Profile.Id);
            string? baseSlot=work.Concept.Direction?.Resolve(work.Slots);
            _baseProfile=baseSlot!=null?mapped[baseSlot].Profile.Id:null;
            _researchWork=work; _resumeResearch=work; _showResearch=true;
            _savedPage=false;
            if (string.IsNullOrWhiteSpace(work.Concept.MissingData)) { _resumeResearch=null; Write(work.Concept); }
            else PrepareConcept(work.Concept);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        { Problem(error.Message); }
    }

    private void DrawResearchPending()
    {
        if (Busy || _pendingResearch.Count==0) return;
        foreach (var work in _pendingResearch.Where(w=>w.Inspired==_inspiredMode).Take(4))
        {
            ImGui.PushID(UiText.Get("research-")+work.Id);
            UiTheme.Section(work.Concept.Title.Length>0?work.Concept.Title:"Investigación guardada");
            UiTheme.Hint(UiText.Format($"{work.Preparations.Count} originales con datos guardados · {work.Passes} rondas · {work.Updated.ToLocalTime():g}"));
            if (ImGui.Button(UiText.Label("Ver investigación"))) { _researchWork=work; _showResearch=true; _savedPage=false; }
            UiTheme.SameLineIfFits("Continuar");
            if (ImGui.Button(UiText.Label("Continuar"))) { _savedPage=false; ResumeResearch(work); }
            UiTheme.SameLineIfFits("Descartar");
            if (ImGui.Button(UiText.Label("Descartar")))
                try { _researchStore.Remove(work.Id); if (_researchWork?.Id==work.Id) _researchWork=null; RefreshWork(); }
                catch (IOException error) { Problem(UiText.Get("No se pudo descartar: ")+error.Message); }
            ImGui.PopID();
        }
    }

    private void DrawResearchResult()
    {
        var work=_researchWork;
        if (work==null) return;
        if (ImGui.GetWindowWidth()<UiTheme.Px(700) || ImGui.GetWindowHeight()<UiTheme.Px(400))
        {
            _quickOriginal=Math.Clamp(_quickOriginal,0,work.Originals.Count-1);
            ImGui.SetNextItemWidth(Math.Max(UiTheme.Px(70),ImGui.GetContentRegionAvail().X-ImGui.CalcTextSize(UiText.Get("Jugar")).X-ImGui.GetStyle().FramePadding.X*2-ImGui.GetStyle().ItemSpacing.X));
            ImGui.Combo("##quick-original",ref _quickOriginal,work.Originals.Select(o=>CanFindOriginal(o,out var source,out _)?source.Profile.Name:o.Profile).ToArray(),work.Originals.Count);
            ImGui.SameLine();
            if (UiTheme.PrimaryButton("Jugar")) OpenResearchOriginal(work.Originals[_quickOriginal].Slot);
            ImGui.Separator();
        }
        if (_observationFor!=work.Id) { _observationFor=work.Id; _humanObservation=work.HumanObservation; }
        UiTheme.Heading(work.Concept.Title.Length>0?work.Concept.Title:UiText.Get("Tu investigación"),
            Busy?"El laboratorio trabaja en una instancia separada. Podés jugar los originales mientras tanto.":"Investigación guardada · quedan comprobaciones antes de construir un programa jugable.", localizeTitle: false);
        ImGui.PushTextWrapPos(0);
        ImGui.TextColored(Busy?UiTheme.Accent:UiTheme.Warning,UiText.Get(_fusionPrepareTask!=null?"01 Originales  /  02 Investigando  /  03 Construcción pendiente":Busy?"01 Originales  /  02 Datos reunidos  /  03 Construyendo":work.Concept.MissingData.Length>0?"02 Requiere comprobaciones  /  03 Construcción pendiente":"02 Datos reunidos  /  03 Construcción pendiente"));
        ImGui.PopTextWrapPos();
        if (Busy || _problem!=null || _manual!=null || _agentDir!=null) DrawProgress();
        if (!Busy && work.StopReason.Length>0) UiTheme.Hint(work.StopReason);
        ImGui.Spacing();
        int columns=ImGui.GetContentRegionAvail().X>=UiTheme.Px(900)?2:1;
        if (!ImGui.BeginTable("research-layout",columns,ImGuiTableFlags.SizingStretchSame)) return;
        ImGui.TableNextColumn(); UiTheme.BeginCard("research-context");
        UiTheme.Section("Los originales están a mano", "Podés abrirlos para jugar o reproducir una observación del laboratorio.");
        foreach (var original in work.Originals)
        {
            ImGui.PushID(UiText.Get("original-")+original.Slot);
            bool found=CanFindOriginal(original,out var source,out string path);
            ImGui.TextWrapped(UiText.Get(found?source.Profile.Name:original.Profile));
            UiTheme.Hint(found?UiText.Format($"{source.Profile.Variables.Count} datos disponibles · {source.Profile.HelpersDoc.Count} funciones documentadas"):"Archivo no encontrado en la carpeta actual.");
            ImGui.BeginDisabled(!found || _playOriginal==null);
            if (UiTheme.PrimaryButton("Jugar original")) OpenResearchOriginal(original.Slot);
            ImGui.EndDisabled(); UiTheme.SameLineIfFits("Abrir analizador");
            ImGui.BeginDisabled(!found || _analyzeOriginal==null);
            if (ImGui.Button(UiText.Label("Abrir analizador"))) OpenResearchOriginal(original.Slot,analyze:true);
            ImGui.EndDisabled();
            var preparation=work.Preparations.FirstOrDefault(p=>p.Profile?.Id==original.Profile);
            if (preparation!=null && ImGui.TreeNode(UiText.Label("Qué se comprobó en este original")))
            {
                foreach(string fact in preparation.CheckedData) ImGui.TextWrapped(UiText.Get(fact));
                foreach(string unresolved in preparation.UnresolvedData) UiTheme.Hint(UiText.Get("Pendiente: ")+unresolved);
                foreach(var experiment in preparation.Experiments.TakeLast(3))
                { ImGui.TextWrapped(UiText.Get(experiment.Description)); UiTheme.Hint(experiment.Outcome); }
                ImGui.TreePop();
            }
            if (found && source.Profile.HelpersDoc.Count>0 && ImGui.TreeNode(UiText.Label("Funciones disponibles para esta edición")))
            { foreach(string helper in source.Profile.HelpersDoc) ImGui.TextWrapped(UiText.Get(helper)); ImGui.TreePop(); }
            ImGui.PopID(); ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        }
        if (!Busy)
        {
            UiTheme.Section("Retomar este proyecto");
            if (UiTheme.PrimaryButton("Continuar investigación")) ResumeResearch(work);
            UiTheme.Hint("Reutiliza lo guardado e inicia otro trabajo con el proveedor elegido. Puede usar varios pedidos; conserva los requisitos pendientes.");
            if (ImGui.CollapsingHeader(UiText.Label("Proveedor y modelo"))) DrawAiChooser("Elegí el método que se usará al continuar.");
            if (ImGui.Button(UiText.Label("Volver a configurar"))) { _showResearch=false; _focusWork=true; }
        }
        UiTheme.EndCard();
        ImGui.TableNextColumn(); UiTheme.BeginCard("research-checklist");
        UiTheme.Section("Plan de comprobación", "Cada guía explica cómo hacer la prueba y qué evidencia hace falta. No tenés que deducir un aviso técnico.");
        foreach (var check in ResearchGuidance.For(work.Concept))
            if (ImGui.CollapsingHeader(check.Title))
            {
                ImGui.TextWrapped(UiText.Get(check.Needed)); ImGui.Spacing();
                for (int i=0;i<check.Steps.Count;i++) { ImGui.TextWrapped(UiText.Get(UiText.Format($"{i+1}. {check.Steps[i]}"))); ImGui.Spacing(); }
                UiTheme.Hint(UiText.Get("Evidencia necesaria: ")+check.Evidence);
            }
        if (ImGui.Button(UiText.Label("Copiar guía completa"))) ImGui.SetClipboardText(ResearchGuidance.Markdown(work));
        if (ImGui.TreeNode(UiText.Label("Requisitos e informe completo")))
        { ImGui.TextWrapped(UiText.Get(work.Concept.MissingData)); ImGui.TextWrapped(UiText.Get(work.Summary)); ImGui.TreePop(); }
        UiTheme.Section("Registrar una observación", "Anotá los pasos, teclas, valores antes/después y lo que ocurrió. La IA tendrá que reproducirlo: una nota no verifica un dato.");
        ImGui.BeginDisabled(Busy);
        ImGui.InputTextMultiline("##human-observation",ref _humanObservation,8000,new(-1,UiTheme.Px(100)));
        if (ImGui.Button(UiText.Label("Guardar observación")))
        {
            work.HumanObservation=_humanObservation;
            try { _researchStore.Save(work); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { Problem(UiText.Get("No se pudo guardar la observación: ")+error.Message); }
        }
        ImGui.EndDisabled(); UiTheme.EndCard(); ImGui.EndTable();
    }
}
