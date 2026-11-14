namespace PolygonLab.UpdateChannel;

/// <summary>Snapshot of the update state, ready to display.</summary>
/// <param name="State">Current state.</param>
/// <param name="Channel">Configured channel.</param>
/// <param name="CurrentVersion">Installed version, or null when the application is not installed by Velopack.</param>
/// <param name="AvailableVersion">Version found on the server, when there is one.</param>
/// <param name="Progress">Download progress from 0 to 100, while downloading.</param>
/// <param name="Message">One sentence describing the state, for the user interface and the logs.</param>
public sealed record UpdateStatus(
    UpdateState State,
    string Channel,
    string? CurrentVersion,
    string? AvailableVersion,
    int? Progress,
    string Message)
{
    /// <summary>True when the user can restart the application to apply a downloaded update.</summary>
    public bool CanRestartToUpdate => State == UpdateState.ReadyToRestart;

    /// <summary>True while a check or a download is running.</summary>
    public bool IsBusy => State is UpdateState.Checking or UpdateState.Downloading;
}
