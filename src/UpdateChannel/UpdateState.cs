namespace PolygonLab.UpdateChannel;

/// <summary>Where the application stands with respect to updates.</summary>
public enum UpdateState
{
    /// <summary>No check has been made yet.</summary>
    Idle,

    /// <summary>The release index is being downloaded.</summary>
    Checking,

    /// <summary>The installed version is the latest one of the channel.</summary>
    UpToDate,

    /// <summary>A newer version exists on the server and has not been downloaded yet.</summary>
    UpdateAvailable,

    /// <summary>The new version is being downloaded.</summary>
    Downloading,

    /// <summary>The new version is downloaded and verified; it is applied when the application restarts.</summary>
    ReadyToRestart,

    /// <summary>The server could not be reached (no network, DNS, TLS, timeout or HTTP error).</summary>
    NetworkError,

    /// <summary>
    /// The application was not installed by Velopack (for example, started from the build folder),
    /// so it cannot update itself.
    /// </summary>
    NotInstalled,

    /// <summary>Any other failure (corrupted package, update already running in another process...).</summary>
    Failed,
}
