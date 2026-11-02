namespace Retro.Update;

public enum StartupUpdateResult { Declined, UpToDate, Started }

/// <summary>Recovery workflow independent of the graphical application and its audio/video libraries.</summary>
public sealed class StartupUpdater(ReleaseClient client)
{
    public async Task<StartupUpdateResult> RunAsync(string installed, Func<bool> check, Func<ReleaseAsset, bool> confirm,
        Action<UpdatePlan> start, IProgress<UpdateProgress>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!check()) return StartupUpdateResult.Declined;
        var manifest = PackageManifest.Read(installed);
        var asset = await client.CheckAsync(ReleaseClient.ParseVersion(manifest.Version), manifest.Runtime, ct);
        if (asset == null) return StartupUpdateResult.UpToDate;
        if (!confirm(asset)) return StartupUpdateResult.Declined;
        var plan = await UpdatePreparation.PrepareAsync(client, asset, installed, progress, ct);
        ct.ThrowIfCancellationRequested();
        start(plan);
        return StartupUpdateResult.Started;
    }
}
