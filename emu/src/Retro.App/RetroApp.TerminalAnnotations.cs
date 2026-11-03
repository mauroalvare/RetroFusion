using Retro.Core;

namespace Retro.App;

public sealed partial class RetroApp
{
    private bool _quitAnnotationsPending;
    private CommandFailure? NeedAnnotations() => _host.Fusion == null && _host.Machine != null && _host.LoadedMedia != null ? null
        : new(CommandProblem.WrongContext, "Las anotaciones por órdenes pertenecen a un original cargado.", "juegos lista tu biblioteca; jugar <número> abre uno.");

    private void WriteAnnotation(CommandContext context, bool label)
    {
        uint address = TerminalAddress(context.Hex("direccion"));
        string? problem;
        try { problem = _code.WriteAnnotation(address, context.Text("texto"), label); }
        catch (ArgumentException error) { throw new CommandFailure(CommandProblem.InvalidArgument, error.Message); }
        context.Detail(InspectionContext());
        if (problem != null) throw new CommandFailure(CommandProblem.Failed, problem, "La anotación sigue en esta sesión; el editor de código muestra la recuperación del archivo.");
        context.Success(label ? "Etiqueta guardada." : "Comentario guardado.");
    }

    private IEnumerable<CommandSpec> AnnotationOrders() =>
    [
        new()
        {
            Id = "analysis.export-notes", Name = "exportar-anotaciones", Aliases = ["export-notes"], Group = "Análisis", Unavailable = NeedAnnotations,
            Summary = "Exporta una copia JSON de las anotaciones actuales sin sobrescribir archivos.", Keywords = "notas exportar copia respaldo guardar",
            Arguments = [new("ruta", ArgumentKind.Path, "Archivo JSON nuevo; entre comillas si tiene espacios.") { Rest = true }],
            Examples = ["exportar-anotaciones \"C:\\Copias\\Notas.json\""],
            Run = context => context.Success(UiText.Get("Copia de anotaciones guardada: ") + _code.ExportAnnotations(context.Text("ruta")))
        },
        new()
        {
            Id = "analysis.notes", Name = "anotaciones", Aliases = ["notes"], Group = "Análisis", Unavailable = NeedAnnotations,
            Summary = "Lista anotaciones o consulta una dirección de la edición actual.", Keywords = "notas comentarios etiquetas rutinas explicaciones",
            Arguments = [new("direccion", ArgumentKind.Hex, "Sin dirección lista hasta 100 anotaciones.") { Optional = true }],
            Examples = ["anotaciones", "anotaciones C000"],
            Run = context =>
            {
                context.Detail(InspectionContext());
                uint[] addresses = context.Has("direccion") ? [TerminalAddress(context.Hex("direccion"))] : _code.AnnotationAddresses().Take(100).ToArray();
                if (addresses.Length == 0) context.Detail("No hay anotaciones guardadas para esta edición.");
                foreach (uint address in addresses)
                {
                    var note = _code.ReadAnnotation(address); context.Heading("$" + address.ToString("X4"));
                    if (note.Label.Length > 0) context.Write(UiText.Format($"Etiqueta: {UiText.Raw(note.Label)}"));
                    if (note.Comment.Length > 0) context.Write(UiText.Format($"Comentario: {UiText.Raw(note.Comment)}"));
                    if (note.Summary.Length > 0) context.Write(UiText.Format($"Explicación guardada: {UiText.Raw(note.Summary)}"));
                    if (note.Pseudocode.Length > 0) context.Write(UiText.Format($"Pseudocódigo: {UiText.Raw(note.Pseudocode)}"));
                    if (note.Label.Length + note.Comment.Length + note.Summary.Length + note.Pseudocode.Length == 0) context.Detail("No hay anotaciones en esa dirección.");
                }
                if (_code.AnnotationProblem.Length > 0) context.Warning(UiText.Message(_code.AnnotationProblem));
                if (_code.HasLegacyAnnotations) context.Detail("Hay anotaciones anteriores por nombre; importar-anotaciones confirmar las incorpora sin reemplazar las actuales.");
            }
        },
        new()
        {
            Id = "analysis.note", Name = "anotar", Aliases = ["note"], Group = "Análisis", Unavailable = NeedAnnotations,
            Summary = "Guarda un comentario manual en una dirección del original actual.", Keywords = "nota comentario rutina guardar",
            Arguments = [new("direccion", ArgumentKind.Hex, "Dirección de esta máquina."), new("texto", ArgumentKind.Text, "Hasta 4096 caracteres.") { Rest = true }],
            Examples = ["anotar C000 Entrada del programa"], Result = "Se comparte con el editor de código; no cambia la memoria ni llama a la IA.",
            Run = context => WriteAnnotation(context, label: false)
        },
        new()
        {
            Id = "analysis.label", Name = "etiquetar", Aliases = ["label"], Group = "Análisis", Unavailable = NeedAnnotations,
            Summary = "Guarda un nombre manual para una dirección del original actual.", Keywords = "etiqueta nombre rutina simbolo",
            Arguments = [new("direccion", ArgumentKind.Hex, "Dirección de esta máquina."), new("texto", ArgumentKind.Text, "Hasta 128 caracteres; los espacios se convierten en guiones bajos.") { Rest = true }],
            Examples = ["etiquetar C000 entrada principal"], Result = "Se comparte con el editor de código; no cambia la memoria ni llama a la IA.",
            Run = context => WriteAnnotation(context, label: true)
        },
        new()
        {
            Id = "analysis.import-notes", Name = "importar-anotaciones", Aliases = ["import-notes"], Group = "Análisis", Unavailable = NeedAnnotations,
            Summary = "Incorpora anotaciones anteriores por nombre con confirmación de edición.", Keywords = "migrar notas anteriores recuperar importar",
            Arguments = [new("confirmar", ArgumentKind.Choice, "Confirma que el archivo anterior corresponde a esta edición.") { Choices = ["confirmar"] }],
            Examples = ["importar-anotaciones confirmar"], Result = "Conserva el archivo anterior y las entradas actuales; no aplica parches a la memoria.",
            Run = context =>
            {
                string? problem = _code.ImportLegacyAnnotations(confirmed: true);
                if (problem != null) throw new CommandFailure(CommandProblem.Failed, problem);
                context.Success("Anotaciones importadas sin reemplazar las actuales. No se aplicaron parches a la memoria.");
            }
        }
    ];
}
