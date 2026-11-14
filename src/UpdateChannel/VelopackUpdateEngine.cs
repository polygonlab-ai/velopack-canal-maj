using System.Diagnostics.CodeAnalysis;
using Velopack;
using Velopack.Sources;

namespace PolygonLab.UpdateChannel;

/// <summary>
/// <see cref="IUpdateEngine"/> backed by Velopack: reads <c>releases.&lt;channel&gt;.json</c> from a plain web folder
/// (<see cref="SimpleWebSource"/>), downloads the package and applies it.
/// </summary>
/// <remarks>
/// This class only forwards calls to Velopack, which needs a real installation, the network and the disk.
/// Every decision (states, errors, logging) lives in <see cref="UpdateService"/>, which is unit tested with a fake engine.
/// The manual end-to-end test (tests/EndToEnd) exercises this class against a real server.
/// </remarks>
[ExcludeFromCodeCoverage(Justification = "Thin adapter over Velopack; covered by the manual end-to-end test, not by unit tests.")]
public sealed class VelopackUpdateEngine : IUpdateEngine
{
    private readonly UpdateManager _manager;

    /// <summary>Creates the engine for the configured folder and channel.</summary>
    public VelopackUpdateEngine(UpdateChannelOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var source = new SimpleWebSource(options.FeedUrl);
        var updateOptions = new UpdateOptions
        {
            // Always explicit: the configured channel wins over the one the installed package was built with.
            ExplicitChannel = options.Channel,
            AllowVersionDowngrade = options.AllowDowngrade,
        };
        _manager = new UpdateManager(source, updateOptions);
    }

    /// <inheritdoc />
    public bool IsInstalled => _manager.IsInstalled;

    /// <inheritdoc />
    public string? CurrentVersion => _manager.IsInstalled ? _manager.CurrentVersion?.ToString() : null;

    /// <inheritdoc />
    public AvailableUpdate? PendingRestart
    {
        get
        {
            var asset = _manager.IsInstalled ? _manager.UpdatePendingRestart : null;
            return asset is null ? null : new AvailableUpdate(asset.Version.ToString(), releaseNotesMarkdown: asset.NotesMarkdown, handle: asset);
        }
    }

    /// <inheritdoc />
    public async Task<AvailableUpdate?> CheckForUpdatesAsync(CancellationToken cancellationToken)
    {
        var info = await _manager.CheckForUpdatesAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        if (info is null)
        {
            return null;
        }

        var target = info.TargetFullRelease;
        return new AvailableUpdate(target.Version.ToString(), info.IsDowngrade, target.NotesMarkdown, info);
    }

    /// <inheritdoc />
    public Task DownloadAsync(AvailableUpdate update, Action<int> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);
        var info = update.Handle as UpdateInfo
            ?? throw new ArgumentException("This update was not found by VelopackUpdateEngine.CheckForUpdatesAsync.", nameof(update));
        return _manager.DownloadUpdatesAsync(info, progress, cancellationToken);
    }

    /// <inheritdoc />
    public void ApplyAndRestart(AvailableUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        VelopackAsset? asset = update.Handle switch
        {
            UpdateInfo info => info.TargetFullRelease,
            VelopackAsset pending => pending,
            _ => null,
        };

        // A null asset tells Velopack to apply the newest downloaded release.
        _manager.ApplyUpdatesAndRestart(asset);
    }
}
