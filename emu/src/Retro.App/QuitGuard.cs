namespace Retro.App;

/// <summary>Closing the program cancels AI work; this says which, so the player can confirm or go back.</summary>
public static class QuitGuard
{
    /// <summary>Null when nothing would be lost; otherwise the notice for the confirmation.</summary>
    public static string? Notice(bool fusion, bool creator, bool analysis, int backgroundTasks = 0)
    {
        var work = new List<string>();
        if (fusion) work.Add("una fusión o creación con originales");
        if (creator) work.Add("un juego nuevo desde cero");
        if (analysis) work.Add("un análisis o una modificación");
        if (backgroundTasks > 0) work.Add(backgroundTasks == 1 ? "una tarea de la consola" : UiText.Format($"{backgroundTasks} tareas de la consola"));
        if (work.Count == 0) return null;
        string list = work.Count == 1 ? work[0] : string.Join(", ", work.Take(work.Count - 1)) + UiText.Get(" y ") + work[^1];
        return UiText.Format($"Hay trabajo en curso: {list}. Si salís ahora se cancela. ") +
            UiText.Get("Se conserva lo que ya estaba guardado: juegos, partidas, perfiles y proyectos.");
    }
}
