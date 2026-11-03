namespace Retro.App;

/// <summary>Queue before offering the interface; acknowledge only when the reader explicitly closes the news.</summary>
internal sealed class ReleaseNews(AppPreferences preferences)
{
    public bool Queue(string target, string? previous, bool force)
    {
        if (ReleaseNotes.ValidVersion(target) is not { } current) return false;
        var end = Version.Parse(current);
        string? pending = ReleaseNotes.ValidVersion(preferences.PendingNewsVersion);
        string? read = ReleaseNotes.ValidVersion(preferences.LastNewsVersion);
        if (pending != null && Version.Parse(pending) > end) return false; // A downgrade cannot discard future unread news.
        bool waiting = pending != null && Version.Parse(pending) <= end;
        if (!force && !waiting && read != null && Version.Parse(read) >= end) return false;

        string? baseline = ReleaseNotes.ValidVersion(previous);
        if (read != null && Version.Parse(read) <= end && (baseline == null || Version.Parse(read) < Version.Parse(baseline))) baseline = read;
        if (waiting)
        {
            string? prior = ReleaseNotes.ValidVersion(preferences.PendingNewsFromVersion);
            if (prior == null || baseline == null) baseline = null;
            else if (Version.Parse(prior) < Version.Parse(baseline)) baseline = prior;
        }
        // An unknown source shows the complete available history, including on a first installation.
        preferences.PendingNewsVersion = current;
        preferences.PendingNewsFromVersion = baseline ?? "";
        preferences.Save();
        return true;
    }

    public void Acknowledge(string version)
    {
        if (preferences.PendingNewsVersion != version) return;
        string? read = ReleaseNotes.ValidVersion(preferences.LastNewsVersion);
        if (read == null || Version.Parse(read) < Version.Parse(version)) preferences.LastNewsVersion = version;
        preferences.PendingNewsVersion = preferences.PendingNewsFromVersion = null;
        preferences.Save();
    }
}
