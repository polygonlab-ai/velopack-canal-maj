using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.Authentication;
using Microsoft.Extensions.Logging;
using Velopack.Exceptions;

namespace PolygonLab.UpdateChannel.Tests;

public sealed class UpdateServiceTests : IDisposable
{
    private readonly FakeUpdateEngine _engine = new();
    private readonly ListLogger<UpdateService> _logger = new();
    private readonly List<UpdateStatus> _changes = [];
    private UpdateService _service;

    public UpdateServiceTests()
    {
        _service = Create(UpdateChannelOptions.Create("https://updates.example.com/sampleapp/", "beta"));
    }

    public void Dispose() => _service.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private UpdateService Create(UpdateChannelOptions options)
    {
        var service = new UpdateService(_engine, options, _logger);
        service.StatusChanged += (_, status) => _changes.Add(status);
        return service;
    }

    private void UseOptions(UpdateChannelOptions options)
    {
        _service.Dispose();
        _service = Create(options);
    }

    private static AvailableUpdate Update(string version = "1.1.0") => new(version, releaseNotesMarkdown: "Notes");

    private bool Logged(int eventId) => _logger.Entries.Any(e => e.EventId.Id == eventId);

    [Fact]
    public void Constructor_StartsIdle()
    {
        var status = _service.Status;

        Assert.Equal(UpdateState.Idle, status.State);
        Assert.Equal("beta", status.Channel);
        Assert.Equal("1.0.0", status.CurrentVersion);
        Assert.Null(status.AvailableVersion);
        Assert.False(status.IsBusy);
        Assert.False(status.CanRestartToUpdate);
        Assert.Equal("beta", _service.Options.Channel);
    }

    [Fact]
    public void Constructor_RejectsNullArguments()
    {
        var options = UpdateChannelOptions.Create("https://updates.example.com/");
        Assert.Throws<ArgumentNullException>(() => new UpdateService(null!, options));
        Assert.Throws<ArgumentNullException>(() => new UpdateService(_engine, null!));
    }

    [Fact]
    public async Task Check_WhenUpToDate_ReportsUpToDate()
    {
        var status = await _service.CheckAsync(Token);

        Assert.Equal(UpdateState.UpToDate, status.State);
        Assert.Contains("latest version of the beta channel", status.Message, StringComparison.Ordinal);
        Assert.Equal([UpdateState.Checking, UpdateState.UpToDate], _changes.Select(c => c.State));
        Assert.True(Logged(4));
        Assert.True(Logged(5));
        Assert.Contains(_logger.Entries, e => e.Message.Contains("https://updates.example.com/sampleapp/releases.beta.json", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Check_WhenNewerVersionExists_ReportsUpdateAvailable()
    {
        _engine.OnCheck = _ => Task.FromResult<AvailableUpdate?>(Update("1.1.0"));

        var status = await _service.CheckAsync(Token);

        Assert.Equal(UpdateState.UpdateAvailable, status.State);
        Assert.Equal("1.1.0", status.AvailableVersion);
        Assert.Equal("Version 1.1.0 is available.", status.Message);
        Assert.True(Logged(6));
    }

    [Fact]
    public async Task Check_WhenNotInstalled_ReportsNotInstalledWithoutCallingTheEngine()
    {
        _engine.IsInstalled = false;

        var status = await _service.CheckAsync(Token);

        Assert.Equal(UpdateState.NotInstalled, status.State);
        Assert.Null(status.CurrentVersion);
        Assert.Contains("not installed by Velopack", status.Message, StringComparison.Ordinal);
        Assert.Equal(0, _engine.CheckCalls);
        Assert.True(Logged(3));
    }

    [Fact]
    public async Task Check_WhenVelopackSaysNotInstalled_ReportsNotInstalled()
    {
        // Velopack only creates this exception internally (no public constructor): build an instance without one.
        var notInstalled = (NotInstalledException)RuntimeHelpers.GetUninitializedObject(typeof(NotInstalledException));
        _engine.OnCheck = _ => throw notInstalled;

        var status = await _service.CheckAsync(Token);

        Assert.Equal(UpdateState.NotInstalled, status.State);
    }

    [Fact]
    public async Task Check_WhenAnUpdateWasDownloadedBefore_IsReadyToRestartAtOnce()
    {
        _engine.PendingRestart = Update("1.2.0");

        var status = await _service.CheckAsync(Token);

        Assert.Equal(UpdateState.ReadyToRestart, status.State);
        Assert.Equal("1.2.0", status.AvailableVersion);
        Assert.True(status.CanRestartToUpdate);
        Assert.Equal(0, _engine.CheckCalls);
        Assert.True(Logged(7));
    }

    public static TheoryData<Exception> NetworkFailures => new()
    {
        new HttpRequestException("No such host is known."),
        new HttpIOException(HttpRequestError.ResponseEnded),
        new SocketException((int)SocketError.ConnectionRefused),
        new TimeoutException(),
        new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout."),
        new AuthenticationException("The remote certificate is invalid."),
        new InvalidOperationException("wrapped", new HttpRequestException("404 (Not Found)")),
        new AggregateException(new InvalidOperationException("other"), new SocketException()),
    };

    [Theory]
    [MemberData(nameof(NetworkFailures))]
    public async Task Check_WhenTheServerIsUnreachable_ReportsNetworkError(Exception failure)
    {
        _engine.OnCheck = _ => Task.FromException<AvailableUpdate?>(failure);

        var status = await _service.CheckAsync(Token);

        Assert.Equal(UpdateState.NetworkError, status.State);
        Assert.Contains("could not be reached", status.Message, StringComparison.Ordinal);
        var entry = Assert.Single(_logger.Entries, e => e.EventId.Id == 10);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Same(failure, entry.Exception);
    }

    [Fact]
    public async Task Check_WhenAnotherFailureOccurs_ReportsFailedWithItsMessage()
    {
        _engine.OnCheck = _ => throw new ChecksumFailedException("SampleApp-1.1.0-full.nupkg", "Checksum mismatch.");

        var status = await _service.CheckAsync(Token);

        Assert.Equal(UpdateState.Failed, status.State);
        Assert.StartsWith("The update check failed: ", status.Message, StringComparison.Ordinal);
        Assert.Equal(LogLevel.Error, Assert.Single(_logger.Entries, e => e.EventId.Id == 11).Level);
    }

    [Fact]
    public async Task Check_WhenAggregateHoldsNoNetworkError_ReportsFailed()
    {
        _engine.OnCheck = _ => throw new AggregateException(new InvalidOperationException("a"), new FormatException("b"));

        Assert.Equal(UpdateState.Failed, (await _service.CheckAsync(Token)).State);
    }

    [Fact]
    public async Task Check_WhenCancelled_RestoresThePreviousStateAndThrows()
    {
        using var cancellation = new CancellationTokenSource();
        _engine.OnCheck = token =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.FromResult<AvailableUpdate?>(null);
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.CheckAsync(cancellation.Token));

        Assert.Equal(UpdateState.Idle, _service.Status.State);
        Assert.Equal(UpdateState.Idle, _changes[^1].State);
        Assert.True(Logged(13));
    }

    [Fact]
    public async Task Download_AfterCheck_ReportsProgressThenReadyToRestart()
    {
        _engine.OnCheck = _ => Task.FromResult<AvailableUpdate?>(Update("1.1.0"));
        _engine.OnDownload = (progress, _) =>
        {
            progress(0);
            progress(40);
            progress(40);
            progress(140);
            return Task.CompletedTask;
        };
        await _service.CheckAsync(Token);
        _changes.Clear();

        var status = await _service.DownloadAsync(Token);

        Assert.Equal(UpdateState.ReadyToRestart, status.State);
        Assert.Equal("Version 1.1.0 is ready. Restart the application to update.", status.Message);
        Assert.Equal([0, 40, 100], _changes.Where(c => c.State == UpdateState.Downloading).Select(c => c.Progress!.Value));
        Assert.Equal("Downloading version 1.1.0... 40 %", _changes[1].Message);
        Assert.True(_changes[0].IsBusy);
        Assert.True(Logged(8));
        Assert.True(Logged(9));
    }

    [Fact]
    public async Task Download_WithoutAvailableUpdate_DoesNothing()
    {
        var status = await _service.DownloadAsync(Token);

        Assert.Equal(UpdateState.Idle, status.State);
        Assert.Equal(0, _engine.DownloadCalls);
        Assert.True(Logged(15));
    }

    [Fact]
    public async Task Download_WhenTheNetworkFails_ReportsNetworkError()
    {
        _engine.OnCheck = _ => Task.FromResult<AvailableUpdate?>(Update());
        _engine.OnDownload = (_, _) => throw new HttpRequestException("Connection reset.");
        await _service.CheckAsync(Token);

        var status = await _service.DownloadAsync(Token);

        Assert.Equal(UpdateState.NetworkError, status.State);
        Assert.Null(status.AvailableVersion);
    }

    [Fact]
    public async Task Download_WhenThePackageIsCorrupted_ReportsFailed()
    {
        _engine.OnCheck = _ => Task.FromResult<AvailableUpdate?>(Update());
        _engine.OnDownload = (_, _) => throw new ChecksumFailedException("SampleApp-1.1.0-full.nupkg", "Checksum mismatch.");
        await _service.CheckAsync(Token);

        var status = await _service.DownloadAsync(Token);

        Assert.Equal(UpdateState.Failed, status.State);
        Assert.StartsWith("The update download failed: ", status.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Download_WhenCancelled_GoesBackToUpdateAvailable()
    {
        using var cancellation = new CancellationTokenSource();
        _engine.OnCheck = _ => Task.FromResult<AvailableUpdate?>(Update("1.1.0"));
        _engine.OnDownload = (progress, token) =>
        {
            progress(10);
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        };
        await _service.CheckAsync(Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.DownloadAsync(cancellation.Token));

        Assert.Equal(UpdateState.UpdateAvailable, _service.Status.State);

        // The update is still known: a new download can start.
        _engine.OnDownload = (_, _) => Task.CompletedTask;
        Assert.Equal(UpdateState.ReadyToRestart, (await _service.DownloadAsync(Token)).State);
    }

    [Fact]
    public async Task CheckAndDownload_DownloadsWhenAVersionIsFound()
    {
        _engine.OnCheck = _ => Task.FromResult<AvailableUpdate?>(Update("2.0.0"));

        var status = await _service.CheckAndDownloadAsync(Token);

        Assert.Equal(UpdateState.ReadyToRestart, status.State);
        Assert.Equal("2.0.0", status.AvailableVersion);
        Assert.Equal(1, _engine.DownloadCalls);
    }

    [Fact]
    public async Task CheckAndDownload_StopsAfterTheCheckWhenUpToDate()
    {
        var status = await _service.CheckAndDownloadAsync(Token);

        Assert.Equal(UpdateState.UpToDate, status.State);
        Assert.Equal(0, _engine.DownloadCalls);
    }

    [Fact]
    public async Task OneOperationAtATime_ASecondRequestIsIgnored()
    {
        var release = new TaskCompletionSource<AvailableUpdate?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _engine.OnCheck = _ => release.Task;

        var first = _service.CheckAsync(Token);
        var second = await _service.CheckAsync(Token);

        Assert.Equal(UpdateState.Checking, second.State);
        Assert.Equal(1, _engine.CheckCalls);
        Assert.True(Logged(12));

        release.SetResult(null);
        Assert.Equal(UpdateState.UpToDate, (await first).State);
    }

    [Fact]
    public async Task RestartToUpdate_AppliesTheDownloadedVersion()
    {
        _engine.OnCheck = _ => Task.FromResult<AvailableUpdate?>(Update("1.1.0"));
        await _service.CheckAndDownloadAsync(Token);

        _service.RestartToUpdate();

        Assert.Equal("1.1.0", Assert.Single(_engine.Applied).Version);
        Assert.True(Logged(14));
    }

    [Fact]
    public async Task RestartToUpdate_AppliesAnUpdateDownloadedInAPreviousSession()
    {
        _engine.PendingRestart = Update("1.2.0");
        await _service.CheckAsync(Token);

        _service.RestartToUpdate();

        Assert.Equal("1.2.0", Assert.Single(_engine.Applied).Version);
    }

    [Fact]
    public async Task RestartToUpdate_WithoutDownloadedUpdate_Throws()
    {
        _engine.OnCheck = _ => Task.FromResult<AvailableUpdate?>(Update());
        await _service.CheckAsync(Token);

        var exception = Assert.Throws<InvalidOperationException>(_service.RestartToUpdate);

        Assert.Contains("UpdateAvailable", exception.Message, StringComparison.Ordinal);
        Assert.Empty(_engine.Applied);
    }

    [Fact]
    public async Task StartupCheck_WhenDisabled_DoesNotCallTheEngine()
    {
        UseOptions(UpdateChannelOptions.Create("https://updates.example.com/", checkOnStartup: false));

        var status = await _service.StartupCheckAsync(Token);

        Assert.Equal(UpdateState.Idle, status.State);
        Assert.Equal(0, _engine.CheckCalls);
        Assert.True(Logged(1));
    }

    [Fact]
    public async Task StartupCheck_ChecksAndDownloadsInTheBackground()
    {
        _engine.OnCheck = _ => Task.FromResult<AvailableUpdate?>(Update("1.1.0"));

        var status = await _service.StartupCheckAsync(Token);

        Assert.Equal(UpdateState.ReadyToRestart, status.State);
        Assert.True(Logged(2));
    }

    [Fact]
    public async Task StartupCheck_ReturnsBeforeTheCheckEnds()
    {
        var release = new TaskCompletionSource<AvailableUpdate?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _engine.OnCheck = _ => release.Task;

        var running = _service.StartupCheckAsync(Token);

        Assert.False(running.IsCompleted);
        release.SetResult(null);
        Assert.Equal(UpdateState.UpToDate, (await running).State);
    }

    [Fact]
    public async Task StartupCheck_NeverThrows_OnNetworkError()
    {
        _engine.OnCheck = _ => throw new HttpRequestException("offline");

        var status = await _service.StartupCheckAsync(Token);

        Assert.Equal(UpdateState.NetworkError, status.State);
    }

    [Fact]
    public async Task StartupCheck_NeverThrows_OnCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        _engine.OnCheck = token =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.FromResult<AvailableUpdate?>(null);
        };

        var status = await _service.StartupCheckAsync(cancellation.Token);

        Assert.Equal(UpdateState.Idle, status.State);
    }

    [Fact]
    public async Task StartupCheck_NeverThrows_WhenAStatusSubscriberFails()
    {
        _service.StatusChanged += (_, _) => throw new InvalidOperationException("subscriber bug");

        var status = await _service.StartupCheckAsync(Token);

        Assert.Equal(UpdateState.Checking, status.State);
        Assert.True(Logged(16));
    }

    [Fact]
    public void AvailableUpdate_KeepsItsValues()
    {
        var handle = new object();
        var update = new AvailableUpdate("1.3.0-beta.1", isDowngrade: true, releaseNotesMarkdown: "# Notes", handle: handle);

        Assert.Equal("1.3.0-beta.1", update.Version);
        Assert.True(update.IsDowngrade);
        Assert.Equal("# Notes", update.ReleaseNotesMarkdown);
        Assert.Same(handle, update.Handle);
        Assert.Throws<ArgumentException>(() => new AvailableUpdate(" "));
    }

    [Fact]
    public void IsNetworkError_IgnoresUnrelatedFailures()
    {
        Assert.False(UpdateService.IsNetworkError(new IOException("Disk full.")));
        Assert.False(UpdateService.IsNetworkError(new InvalidOperationException("x", new FormatException())));
    }
}
