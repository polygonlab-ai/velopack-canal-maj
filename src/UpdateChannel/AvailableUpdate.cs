namespace PolygonLab.UpdateChannel;

/// <summary>A version found on the update server (or already downloaded and waiting for a restart).</summary>
public sealed class AvailableUpdate
{
    /// <summary>Describes an update.</summary>
    /// <param name="version">Version number, for example <c>1.2.0</c> or <c>1.3.0-beta.1</c>.</param>
    /// <param name="isDowngrade">True when the version is lower than (or equal to) the installed one, for example after a channel switch.</param>
    /// <param name="releaseNotesMarkdown">Release notes passed to <c>vpk pack --releaseNotes</c>, if any.</param>
    /// <param name="handle">Object owned by the <see cref="IUpdateEngine"/> that found the update (for Velopack, its own update description).</param>
    public AvailableUpdate(string version, bool isDowngrade = false, string? releaseNotesMarkdown = null, object? handle = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        Version = version;
        IsDowngrade = isDowngrade;
        ReleaseNotesMarkdown = releaseNotesMarkdown;
        Handle = handle;
    }

    /// <summary>Version number.</summary>
    public string Version { get; }

    /// <summary>True when the version is not higher than the installed one.</summary>
    public bool IsDowngrade { get; }

    /// <summary>Release notes in Markdown, if the release has some.</summary>
    public string? ReleaseNotesMarkdown { get; }

    /// <summary>Engine-specific object; only the engine that created this instance reads it.</summary>
    public object? Handle { get; }
}
