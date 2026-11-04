using System.Globalization;

namespace Retro.App;

/// <summary>
/// References, patches, exports and variable edits by orders: the same services and transactions as
/// Código y rutinas and Modificar y analizar, on the original loaded right now.
/// </summary>
public sealed partial class RetroApp
{
    private CommandFailure? NeedSmallOriginal() => NeedAnnotations() ?? (_host.Machine!.Memory.AddressBits <= 16 ? null
        : new(CommandProblem.WrongContext, "Las variables por órdenes están disponibles en máquinas de 8 bits.", "Modificar y analizar muestra lo que admite esta máquina."));

    /// <summary>Runs a code-workshop service and turns its expected refusals into terminal failures.</summary>
    private T CodeService<T>(Func<T> action)
    {
        try { return action(); }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or InvalidDataException)
        { throw new CommandFailure(CommandProblem.InvalidArgument, UiText.Message(error.Message)); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        { throw new CommandFailure(CommandProblem.Failed, UiText.Message(error.Message), "Elegí otra ruta o revisá permisos y espacio."); }
    }

    private void SavingWarning(CommandContext context)
    {
        if (_code.AnnotationProblem.Length > 0) context.Warning(UiText.Message(_code.AnnotationProblem));
    }

    private static string Bytes(byte[] bytes) => string.Join(" ", bytes.Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));

    private IEnumerable<CommandSpec> CodeOrders() =>
    [
        new()
        {
            Id = "analysis.references", Name = "referencias", Aliases = ["refs", "xref"], Group = "Análisis", Unavailable = NeedAnnotations,
            Summary = "Muestra la rutina de una dirección, quién la llama, a quién llama y qué instrucciones la nombran.", Keywords = "llamadas saltos rutina usos lectura escritura variable",
            Arguments = [new("direccion", ArgumentKind.Hex, "Código o dato de esta máquina.")],
            Examples = ["referencias C000", "referencias 00D5"], Result = "Usa las rutinas reconstruidas; jugar un rato con la consola abierta encuentra más.",
            Run = context =>
            {
                uint address = TerminalAddress(context.Hex("direccion"));
                var refs = CodeService(() => _code.References(address));
                context.Detail(InspectionContext());
                if (refs.Routine is { } routine)
                {
                    context.Heading(UiText.Format($"{UiText.Raw(routine.Name)} (${routine.Address:X4}-${refs.End:X4}, {refs.Instructions} instrucciones{(refs.Executed ? "" : UiText.Get(", sin ejecutar todavía"))})"));
                    context.Write(UiText.Get("Llama a: ") + (refs.Calls.Count == 0 ? UiText.Get("ninguna rutina conocida") : string.Join(", ", refs.Calls.Take(24).Select(link => link.Name))));
                    context.Write(UiText.Get("La llaman: ") + (refs.Callers.Count == 0 ? UiText.Get("ninguna rutina conocida") : string.Join(", ", refs.Callers.Take(24).Select(link => link.Name))));
                }
                else context.Detail(UiText.Format($"${address:X4} no está dentro de una rutina conocida ({refs.RoutineCount} reconstruidas)."));
                if (refs.MentionCount == 0) context.Detail("Ninguna instrucción conocida nombra esa dirección.");
                else
                {
                    context.Heading(UiText.Format($"Instrucciones que nombran ${address:X4}: {refs.MentionCount}"));
                    foreach (var mention in refs.Mentions) context.Write(UiText.Format($"${mention.Address:X4}  {UiText.Raw(mention.Name)}"));
                    if (refs.MentionCount > refs.Mentions.Count) context.Detail(UiText.Format($"y {refs.MentionCount - refs.Mentions.Count} más."));
                }
            }
        },
        new()
        {
            Id = "analysis.patches", Name = "parches", Aliases = ["patches"], Group = "Análisis", Unavailable = NeedAnnotations,
            Summary = "Lista los parches guardados de esta edición, con sus bytes y si la partida los usa ahora.", Keywords = "cambios codigo bytes respaldo",
            Run = context =>
            {
                var patches = CodeService(() => { var list = _code.SavedPatches(out int damaged); return (list, damaged); });
                context.Detail(InspectionContext());
                if (patches.list.Count == 0 && patches.damaged == 0) context.Detail("No hay parches guardados para esta edición.");
                else context.Table([new("Dirección"), new("Original"), new("Parche"), new("Estado"), new("Rutina", Optional: true)],
                    patches.list.Select(patch => new[] { "$" + patch.Address.ToString("X4", CultureInfo.InvariantCulture), Bytes(patch.Original), Bytes(patch.Replacement),
                        patch.Applied ? "aplicado" : "no aplicado", patch.Routine }));
                if (patches.damaged > 0) context.Warning(UiText.Format($"{patches.damaged} parches guardados están incompletos; se conservan sin aplicarlos."));
                context.Detail("parche <dirección> <bytes> prepara uno; deshacer-parche <dirección> restaura los bytes originales.");
            }
        },
        new()
        {
            Id = "analysis.patch", Name = "parche", Aliases = ["patch"], Group = "Análisis", Unavailable = NeedAnnotations,
            Summary = "Prepara un cambio de bytes y muestra el código antes y después; con aplicar, lo escribe.", Keywords = "cambiar bytes nop anular codigo modificar",
            Arguments = [new("direccion", ArgumentKind.Hex, "Primera dirección a cambiar."), new("bytes", ArgumentKind.Text, "Pares hexadecimales; terminá con aplicar para escribirlos.") { Rest = true }],
            Examples = ["parche C000 EA EA", "parche C000 EA EA aplicar"], Errors = ["bytes inválidos", "fuera de la memoria", "se superpone con otro parche"],
            Result = "Cambia la partida en memoria y guarda un respaldo; el archivo original no se toca.",
            Run = context =>
            {
                uint address = TerminalAddress(context.Hex("direccion"));
                var words = context.Text("bytes").Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
                bool apply = words.Count > 0 && CommandLine.Fold(words[^1]) is "aplicar" or "apply";
                if (apply) words.RemoveAt(words.Count - 1);
                string hex = string.Join(' ', words);
                var preview = CodeService(() => _code.PreparePatch(address, hex));
                context.Detail(InspectionContext());
                context.Heading(UiText.Get("Antes"));
                foreach (string line in preview.BeforeCode) context.Write(line);
                context.Heading(UiText.Get("Después"));
                foreach (string line in preview.AfterCode) context.Write(line);
                if (!apply)
                {
                    context.Detail(UiText.Format($"Nada cambió todavía. Para escribirlo: parche {address:X4} {hex} aplicar"));
                    return;
                }
                string done = CodeService(() => _code.ApplyPatch(address, hex));
                SavingWarning(context);
                context.Success(done);
            }
        },
        new()
        {
            Id = "analysis.undo-patch", Name = "deshacer-parche", Aliases = ["undo-patch"], Group = "Análisis", Unavailable = NeedAnnotations,
            Summary = "Restaura los bytes originales de un parche guardado y lo quita de la lista.", Keywords = "revertir quitar parche original",
            Arguments = [new("direccion", ArgumentKind.Hex, "Dirección del parche, como la muestra parches.")],
            Examples = ["deshacer-parche C000"],
            Run = context =>
            {
                uint address = TerminalAddress(context.Hex("direccion"));
                string done = CodeService(() => _code.UndoPatch(address));
                SavingWarning(context);
                context.Success(done);
            }
        },
        new()
        {
            Id = "analysis.reapply-patches", Name = "reaplicar-parches", Aliases = ["reapply-patches"], Group = "Análisis", Unavailable = NeedAnnotations,
            Summary = "Vuelve a escribir en la partida todos los parches guardados de esta edición.", Keywords = "aplicar todos parches cargar",
            Run = context =>
            {
                var (applied, total) = CodeService(() => _code.ReapplyPatches());
                if (total == 0) { context.Detail("No hay parches guardados para esta edición."); return; }
                context.Success(UiText.Format($"Parches aplicados: {applied} de {total}."));
                if (applied < total) context.Warning("Algunos rangos no admiten escritura o su respaldo está incompleto; la partida no cambió en esos.");
            }
        },
        new()
        {
            Id = "analysis.export-asm", Name = "exportar-asm", Aliases = ["export-asm"], Group = "Análisis", Unavailable = NeedAnnotations,
            Summary = "Guarda el desensamblado con nombres, comentarios y variables en un archivo nuevo.", Keywords = "codigo listado ensamblador guardar",
            Arguments = [new("ruta", ArgumentKind.Path, "Archivo nuevo; sin ruta usa la carpeta de exportaciones y numera el nombre.") { Optional = true, Rest = true }],
            Examples = ["exportar-asm", "exportar-asm \"C:\\Copias\\juego.asm\""], Result = "Nunca reemplaza un archivo; un fallo no deja un archivo a medias.",
            Run = context =>
            {
                string path = CodeService(() => _code.ExportListing(context.Optional("ruta")));
                context.Success(UiText.Get("Código exportado: ") + path);
            }
        },
        new()
        {
            Id = "analysis.export-profile", Name = "exportar-perfil", Aliases = ["export-profile"], Group = "Análisis", Unavailable = NeedAnnotations,
            Summary = "Guarda una copia JSON del perfil de este original: variables, controles y arranque.", Keywords = "perfil variables compartir copia",
            Arguments = [new("ruta", ArgumentKind.Path, "Archivo JSON nuevo; entre comillas si tiene espacios.") { Rest = true }],
            Examples = ["exportar-perfil \"C:\\Copias\\perfil.json\""], Result = "Nunca reemplaza un archivo; el perfil en uso no cambia.",
            Run = context =>
            {
                string path = CodeService(() => _code.ExportProfile(context.Text("ruta")));
                context.Success(UiText.Get("Perfil exportado: ") + path);
            }
        },
        new()
        {
            Id = "analysis.add-variable", Name = "agregar-variable", Aliases = ["add-variable"], Group = "Análisis", Unavailable = NeedSmallOriginal,
            Summary = "Agrega al perfil un dato de un byte encontrado con rastrear.", Keywords = "variable nueva perfil rastrear dirección",
            Arguments = [new("direccion", ArgumentKind.Hex, "Dirección de la RAM."), new("nombre", ArgumentKind.Word, "Letras, números y guiones bajos."),
                new("descripcion", ArgumentKind.Text, "Qué representa, según lo que comprobaste.") { Optional = true, Rest = true }],
            Examples = ["agregar-variable 00D5 vidas baja al perder una vida"], Result = "Se guarda en el perfil de esta edición, igual que desde el buscador.",
            Run = context =>
            {
                uint address = TerminalAddress(context.Hex("direccion"));
                string done = CodeService(() => _code.AddVariable(address, context.Text("nombre"), context.Optional("descripcion") ?? ""));
                _host.RefreshLoadedProfile();
                context.Success(done);
            }
        },
        new()
        {
            Id = "analysis.set-variable", Name = "cambiar-variable", Aliases = ["set-variable"], Group = "Análisis", Unavailable = NeedSmallOriginal,
            Summary = "Cambia el valor de una variable del perfil en la partida y permite congelarla.", Keywords = "modificar valor congelar vidas puntos",
            Arguments = [new("nombre", ArgumentKind.Word, "Como la lista variables."), new("valor", ArgumentKind.Number, "Valor entero dentro de su rango.") { Min = int.MinValue, Max = int.MaxValue },
                new("modo", ArgumentKind.Choice, "congelar la mantiene; soltar la libera.") { Optional = true, Choices = ["congelar", "soltar"] }],
            Examples = ["cambiar-variable vidas 5", "cambiar-variable vidas 9 congelar"], Result = "deshacer-variable revierte el último cambio; deshacer-variable todo restaura la partida.",
            Run = context =>
            {
                bool? freeze = context.Optional("modo") is { } mode ? CommandLine.Fold(mode) == "congelar" : null;
                string done = CodeService(() => _code.SetVariable(context.Text("nombre"), context.Number("valor"), freeze));
                context.Success(UiText.Get("Cambio aplicado: ") + done);
            }
        },
        new()
        {
            Id = "analysis.undo-variable", Name = "deshacer-variable", Aliases = ["undo-variable"], Group = "Análisis", Unavailable = NeedSmallOriginal,
            Summary = "Deshace el último cambio de variables o restaura la partida previa a todos.", Keywords = "revertir restaurar congelados",
            Arguments = [new("alcance", ArgumentKind.Choice, "todo vuelve al momento anterior al primer cambio.") { Optional = true, Choices = ["todo"] }],
            Examples = ["deshacer-variable", "deshacer-variable todo"],
            Run = context => context.Success(CodeService(() => _code.UndoVariables(all: context.Has("alcance"))))
        }
    ];
}
