namespace PolygonLab.UpdateChannel.Tests;

/// <summary>Scriptable <see cref="IUpdateEngine"/>: no network, no disk.</summary>
internal sealed class FakeUpdateEngine : IUpdateEngine
{
    public bool IsInstalled { get; set; } = true;

    public string? CurrentVersion { get; set; } = "1.0.0";

    public AvailableUpdate? PendingRestart { get; set; }

    /// <summary>What the next check returns, or throws.</summary>
    public Func<CancellationToken, Task<AvailableUpdate?>> OnCheck { get; set; } = _ => Task.FromResult<AvailableUpdate?>(null);

    /// <summary>What the next download does; receives the progress callback.</summary>
    public Func<Action<int>, CancellationToken, Task> OnDownload { get; set; } = (progress, _) =>
    {
        progress(50);
        progress(100);
        return Task.CompletedTask;
    };

    public int CheckCalls { get; private set; }

    public int DownloadCalls { get; private set; }

    public List<AvailableUpdate> Applied { get; } = [];

    public Task<AvailableUpdate?> CheckForUpdatesAsync(CancellationToken cancellationToken)
    {
        CheckCalls++;
        return OnCheck(cancellationToken);
    }

    public Task DownloadAsync(AvailableUpdate update, Action<int> progress, CancellationToken cancellationToken)
    {
        DownloadCalls++;
        return OnDownload(progress, cancellationToken);
    }

    public void ApplyAndRestart(AvailableUpdate update) => Applied.Add(update);
}
