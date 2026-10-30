namespace Retro.App;

public sealed partial class RetroApp
{
    private bool ShowEmptyPreview => (_creator.IsOpen || _studio.IsOpen) && _host.LoadedMedia == null && _host.Fusion == null;
}
