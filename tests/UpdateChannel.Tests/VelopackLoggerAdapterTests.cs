using Microsoft.Extensions.Logging;
using Velopack.Logging;

namespace PolygonLab.UpdateChannel.Tests;

public sealed class VelopackLoggerAdapterTests
{
    [Theory]
    [InlineData(VelopackLogLevel.Trace, LogLevel.Trace)]
    [InlineData(VelopackLogLevel.Debug, LogLevel.Debug)]
    [InlineData(VelopackLogLevel.Information, LogLevel.Information)]
    [InlineData(VelopackLogLevel.Warning, LogLevel.Warning)]
    [InlineData(VelopackLogLevel.Error, LogLevel.Error)]
    [InlineData(VelopackLogLevel.Critical, LogLevel.Critical)]
    [InlineData((VelopackLogLevel)42, LogLevel.Information)]
    public void ToLogLevel_MapsEveryLevel(VelopackLogLevel input, LogLevel expected)
        => Assert.Equal(expected, VelopackLoggerAdapter.ToLogLevel(input));

    [Fact]
    public void Log_ForwardsTheMessageAsAValue()
    {
        var logger = new ListLogger<VelopackLoggerAdapterTests>();
        var failure = new InvalidOperationException("boom");

        new VelopackLoggerAdapter(logger).Log(VelopackLogLevel.Warning, "Feed {not a template}", failure);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal("Velopack: Feed {not a template}", entry.Message);
        Assert.Same(failure, entry.Exception);
        Assert.Equal(100, entry.EventId.Id);
    }

    [Fact]
    public void Log_SkipsDisabledLevelsAndNullMessages()
    {
        var logger = new ListLogger<VelopackLoggerAdapterTests> { MinimumLevel = LogLevel.Information };
        var adapter = new VelopackLoggerAdapter(logger);

        adapter.Log(VelopackLogLevel.Debug, "hidden", null);
        adapter.Log(VelopackLogLevel.Error, null, null);

        Assert.Equal("Velopack: ", Assert.Single(logger.Entries).Message);
    }

    [Fact]
    public void Constructor_RejectsANullLogger()
        => Assert.Throws<ArgumentNullException>(() => new VelopackLoggerAdapter(null!));
}
