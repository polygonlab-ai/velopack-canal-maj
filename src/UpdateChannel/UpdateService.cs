using System.Net.Sockets;
using System.Security.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Velopack.Exceptions;

namespace PolygonLab.UpdateChannel;

/// <summary>
/// Checks, downloads and applies updates with explicit states, never throwing for an expected failure
/// (network down, application not installed...): the failure becomes a <see cref="UpdateStatus"/>.
/// </summary>
/// <remarks>
/// <para>Thread safety: one operation at a time. A request made while another one runs is ignored and returns
/// the current status. <see cref="StatusChanged"/> is raised on the thread that runs the operation, which may
/// not be the user interface thread.</para>
/// <para>Logging: every step is written to the <see cref="ILogger"/> given to the constructor; this class writes
/// no file of its own.</para>
/// </remarks>
public sealed partial class UpdateService : IDisposable
{
    private readonly IUpdateEngine _engine;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Lock _sync = new();
    private UpdateStatus _status;
    private AvailableUpdate? _update;

    /// <summary>Creates the service.</summary>
    /// <param name="engine">Update engine, usually <see cref="VelopackUpdateEngine"/>.</param>
    /// <param name="options">Validated channel settings.</param>
    /// <param name="logger">Destination of the step-by-step log; nothing is logged when null.</param>
    public UpdateService(IUpdateEngine engine, UpdateChannelOptions options, ILogger<UpdateService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(options);
        _engine = engine;
        Options = options;
        _logger = logger ?? (ILogger)NullLogger.Instance;
        _status = Describe(UpdateState.Idle, null, null, "Updates have not been checked yet.");
    }

    /// <summary>Raised after every change of <see cref="Status"/>, including download progress.</summary>
    public event EventHandler<UpdateStatus>? StatusChanged;

    /// <summary>Channel settings used by this service.</summary>
    public UpdateChannelOptions Options { get; }

    /// <summary>Latest status.</summary>
    public UpdateStatus Status
    {
        get
        {
            lock (_sync)
            {
                return _status;
            }
        }
    }

    /// <summary>
    /// Starts the automatic check in the background when <see cref="UpdateChannelOptions.CheckOnStartup"/> is true:
    /// check, then download if a newer version exists. Returns at once; the returned task never fails.
    /// </summary>
    public Task<UpdateStatus> StartupCheckAsync(CancellationToken cancellationToken = default)
    {
        if (!Options.CheckOnStartup)
        {
            LogStartupCheckDisabled(_logger);
            return Task.FromResult(Status);
        }

        LogStartupCheckScheduled(_logger, Options.Channel);

        // Task.Run: the caller (often the UI thread at startup) never waits for the network.
        return Task.Run(() => RunWithoutThrowingAsync(cancellationToken), CancellationToken.None);
    }

    /// <summary>Downloads the release index and reports whether a newer version exists.</summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public Task<UpdateStatus> CheckAsync(CancellationToken cancellationToken = default)
        => RunExclusiveAsync(CheckCoreAsync, cancellationToken);

    /// <summary>Downloads the update found by the last check.</summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public Task<UpdateStatus> DownloadAsync(CancellationToken cancellationToken = default)
        => RunExclusiveAsync(DownloadCoreAsync, cancellationToken);

    /// <summary>Checks, then downloads when a newer version exists.</summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public Task<UpdateStatus> CheckAndDownloadAsync(CancellationToken cancellationToken = default)
        => RunExclusiveAsync(
            async token =>
            {
                var status = await CheckCoreAsync(token).ConfigureAwait(false);
                return status.State == UpdateState.UpdateAvailable
                    ? await DownloadCoreAsync(token).ConfigureAwait(false)
                    : status;
            },
            cancellationToken);

    /// <summary>
    /// Exits the application, applies the downloaded update and starts the new version.
    /// Save the user's work before calling it.
    /// </summary>
    /// <exception cref="InvalidOperationException">No downloaded update is waiting (state is not <see cref="UpdateState.ReadyToRestart"/>).</exception>
    public void RestartToUpdate()
    {
        AvailableUpdate? update;
        lock (_sync)
        {
            update = _status.State == UpdateState.ReadyToRestart ? _update : null;
        }

        if (update is null)
        {
            throw new InvalidOperationException("No downloaded update is waiting for a restart (state: " + Status.State + ").");
        }

        LogRestarting(_logger, update.Version);
        _engine.ApplyAndRestart(update);
    }

    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();

    private async Task<UpdateStatus> RunWithoutThrowingAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await CheckAndDownloadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Status;
        }
        catch (Exception ex)
        {
            // The startup check runs unattended in the background: no exception may escape it, whatever its type.
            LogUnexpected(_logger, ex);
            return Status;
        }
    }

    private async Task<UpdateStatus> RunExclusiveAsync(Func<CancellationToken, Task<UpdateStatus>> operation, CancellationToken cancellationToken)
    {
        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            LogBusy(_logger);
            return Status;
        }

        try
        {
            return await operation(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<UpdateStatus> CheckCoreAsync(CancellationToken cancellationToken)
    {
        if (!_engine.IsInstalled)
        {
            LogNotInstalled(_logger);
            return Publish(UpdateState.NotInstalled, null, null,
                "This copy was not installed by Velopack (for example, it runs from the build folder): updates are disabled.");
        }

        var pending = _engine.PendingRestart;
        if (pending is not null)
        {
            LogPendingFound(_logger, pending.Version);
            return Publish(UpdateState.ReadyToRestart, pending, null, ReadyMessage(pending));
        }

        var previous = Status;
        LogCheckStarted(_logger, Options.ReleaseIndexUrl, _engine.CurrentVersion);
        Publish(UpdateState.Checking, null, null, "Checking for updates on the " + Options.Channel + " channel...");
        try
        {
            var update = await _engine.CheckForUpdatesAsync(cancellationToken).ConfigureAwait(false);
            if (update is null)
            {
                LogUpToDate(_logger, _engine.CurrentVersion, Options.Channel);
                return Publish(UpdateState.UpToDate, null, null, "You have the latest version of the " + Options.Channel + " channel.");
            }

            LogUpdateFound(_logger, update.Version, Options.Channel, update.IsDowngrade);
            return Publish(UpdateState.UpdateAvailable, update, null, "Version " + update.Version + " is available.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            LogCancelled(_logger, "check");
            Restore(previous);
            throw;
        }
        catch (Exception ex)
        {
            // Every failure becomes an explicit state; the caller decides what to show.
            return Fail(ex, "check");
        }
    }

    private async Task<UpdateStatus> DownloadCoreAsync(CancellationToken cancellationToken)
    {
        UpdateStatus previous;
        AvailableUpdate? update;
        lock (_sync)
        {
            previous = _status;
            update = _status.State == UpdateState.UpdateAvailable ? _update : null;
        }

        if (update is null)
        {
            LogNothingToDownload(_logger, previous.State);
            return previous;
        }

        LogDownloadStarted(_logger, update.Version);
        Publish(UpdateState.Downloading, update, 0, DownloadMessage(update, 0));
        try
        {
            await _engine.DownloadAsync(update, percent => ReportProgress(update, percent), cancellationToken).ConfigureAwait(false);
            LogDownloadCompleted(_logger, update.Version);
            return Publish(UpdateState.ReadyToRestart, update, null, ReadyMessage(update));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            LogCancelled(_logger, "download");
            Restore(previous);
            throw;
        }
        catch (Exception ex)
        {
            // Every failure becomes an explicit state; the caller decides what to show.
            return Fail(ex, "download");
        }
    }

    private void ReportProgress(AvailableUpdate update, int percent)
    {
        var value = Math.Clamp(percent, 0, 100);
        lock (_sync)
        {
            if (_status.State != UpdateState.Downloading || _status.Progress == value)
            {
                return;
            }
        }

        Publish(UpdateState.Downloading, update, value, DownloadMessage(update, value));
    }

    private UpdateStatus Fail(Exception exception, string operation)
    {
        if (exception is NotInstalledException)
        {
            LogNotInstalled(_logger);
            return Publish(UpdateState.NotInstalled, null, null,
                "This copy was not installed by Velopack (for example, it runs from the build folder): updates are disabled.");
        }

        if (IsNetworkError(exception))
        {
            LogNetworkError(_logger, operation, exception);
            return Publish(UpdateState.NetworkError, null, null,
                "The update server could not be reached. Check the network connection and try again.");
        }

        LogFailed(_logger, operation, exception);
        return Publish(UpdateState.Failed, null, null, "The update " + operation + " failed: " + exception.Message);
    }

    /// <summary>True when the exception, or one of its inner exceptions, comes from the network.</summary>
    internal static bool IsNetworkError(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is HttpRequestException or HttpIOException or SocketException or TimeoutException
                or TaskCanceledException or AuthenticationException)
            {
                return true;
            }

            if (current is AggregateException aggregate && aggregate.InnerExceptions.Any(IsNetworkError))
            {
                return true;
            }
        }

        return false;
    }

    private UpdateStatus Publish(UpdateState state, AvailableUpdate? update, int? progress, string message)
    {
        var status = Describe(state, update?.Version, progress, message);
        lock (_sync)
        {
            _status = status;
            if (state is UpdateState.UpdateAvailable or UpdateState.ReadyToRestart)
            {
                _update = update;
            }
            else if (state != UpdateState.Downloading)
            {
                _update = null;
            }
        }

        StatusChanged?.Invoke(this, status);
        return status;
    }

    private void Restore(UpdateStatus previous)
    {
        lock (_sync)
        {
            _status = previous;
        }

        StatusChanged?.Invoke(this, previous);
    }

    private UpdateStatus Describe(UpdateState state, string? availableVersion, int? progress, string message)
        => new(state, Options.Channel, _engine.IsInstalled ? _engine.CurrentVersion : null, availableVersion, progress, message);

    private static string DownloadMessage(AvailableUpdate update, int percent)
        => "Downloading version " + update.Version + "... " + percent.ToString(System.Globalization.CultureInfo.InvariantCulture) + " %";

    private static string ReadyMessage(AvailableUpdate update)
        => "Version " + update.Version + " is ready. Restart the application to update.";

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Automatic update check on startup is disabled.")]
    private static partial void LogStartupCheckDisabled(ILogger logger);

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug, Message = "Automatic update check started in the background (channel {Channel}).")]
    private static partial void LogStartupCheckScheduled(ILogger logger, string channel);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Application not installed by Velopack: update check skipped.")]
    private static partial void LogNotInstalled(ILogger logger);

    [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "Checking for updates at {IndexUrl} (installed version {CurrentVersion}).")]
    private static partial void LogCheckStarted(ILogger logger, Uri indexUrl, string? currentVersion);

    [LoggerMessage(EventId = 5, Level = LogLevel.Information, Message = "Up to date: version {CurrentVersion} is the latest on channel {Channel}.")]
    private static partial void LogUpToDate(ILogger logger, string? currentVersion, string channel);

    [LoggerMessage(EventId = 6, Level = LogLevel.Information, Message = "Update found: version {Version} on channel {Channel} (downgrade: {IsDowngrade}).")]
    private static partial void LogUpdateFound(ILogger logger, string version, string channel, bool isDowngrade);

    [LoggerMessage(EventId = 7, Level = LogLevel.Information, Message = "Version {Version} was downloaded earlier and waits for a restart.")]
    private static partial void LogPendingFound(ILogger logger, string version);

    [LoggerMessage(EventId = 8, Level = LogLevel.Information, Message = "Downloading version {Version}.")]
    private static partial void LogDownloadStarted(ILogger logger, string version);

    [LoggerMessage(EventId = 9, Level = LogLevel.Information, Message = "Version {Version} downloaded and verified; it is applied on restart.")]
    private static partial void LogDownloadCompleted(ILogger logger, string version);

    [LoggerMessage(EventId = 10, Level = LogLevel.Warning, Message = "Update server unreachable during the {Operation}.")]
    private static partial void LogNetworkError(ILogger logger, string operation, Exception exception);

    [LoggerMessage(EventId = 11, Level = LogLevel.Error, Message = "Update {Operation} failed.")]
    private static partial void LogFailed(ILogger logger, string operation, Exception exception);

    [LoggerMessage(EventId = 12, Level = LogLevel.Debug, Message = "An update operation is already running: request ignored.")]
    private static partial void LogBusy(ILogger logger);

    [LoggerMessage(EventId = 13, Level = LogLevel.Information, Message = "Update {Operation} cancelled.")]
    private static partial void LogCancelled(ILogger logger, string operation);

    [LoggerMessage(EventId = 14, Level = LogLevel.Information, Message = "Restarting to apply version {Version}.")]
    private static partial void LogRestarting(ILogger logger, string version);

    [LoggerMessage(EventId = 15, Level = LogLevel.Debug, Message = "Nothing to download (state {State}).")]
    private static partial void LogNothingToDownload(ILogger logger, UpdateState state);

    [LoggerMessage(EventId = 16, Level = LogLevel.Error, Message = "Unexpected error during the automatic update check.")]
    private static partial void LogUnexpected(ILogger logger, Exception exception);
}
