namespace PolygonLab.UpdateChannel;

/// <summary>
/// The few update operations the application needs. <see cref="VelopackUpdateEngine"/> implements them with Velopack;
/// tests use a fake, so they never touch the network or the disk.
/// </summary>
public interface IUpdateEngine
{
    /// <summary>True when the application was installed by Velopack and can update itself.</summary>
    bool IsInstalled { get; }

    /// <summary>Installed version, or null when <see cref="IsInstalled"/> is false.</summary>
    string? CurrentVersion { get; }

    /// <summary>An update downloaded earlier and not applied yet, or null.</summary>
    AvailableUpdate? PendingRestart { get; }

    /// <summary>Downloads the release index of the channel and returns the newer version, or null when up to date.</summary>
    Task<AvailableUpdate?> CheckForUpdatesAsync(CancellationToken cancellationToken);

    /// <summary>Downloads and verifies the update; <paramref name="progress"/> receives values from 0 to 100.</summary>
    Task DownloadAsync(AvailableUpdate update, Action<int> progress, CancellationToken cancellationToken);

    /// <summary>Exits the application, applies the downloaded update and starts the new version.</summary>
    void ApplyAndRestart(AvailableUpdate update);
}
