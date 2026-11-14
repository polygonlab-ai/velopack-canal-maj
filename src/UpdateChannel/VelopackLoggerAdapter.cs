using Microsoft.Extensions.Logging;
using Velopack.Logging;

namespace PolygonLab.UpdateChannel;

/// <summary>
/// Sends Velopack's own diagnostic messages to a standard <see cref="ILogger"/>.
/// Pass it to <c>VelopackApp.Build().SetLogger(...)</c> at startup.
/// </summary>
public sealed partial class VelopackLoggerAdapter : IVelopackLogger
{
    private readonly ILogger _logger;

    /// <summary>Creates the adapter.</summary>
    public VelopackLoggerAdapter(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public void Log(VelopackLogLevel logLevel, string? message, Exception? exception)
    {
        var level = ToLogLevel(logLevel);
        if (_logger.IsEnabled(level))
        {
            // The message is passed as a value, never as a template: braces in it are kept as they are.
            LogVelopack(_logger, level, message ?? string.Empty, exception);
        }
    }

    /// <summary>Velopack level to standard level.</summary>
    internal static LogLevel ToLogLevel(VelopackLogLevel level) => level switch
    {
        VelopackLogLevel.Trace => LogLevel.Trace,
        VelopackLogLevel.Debug => LogLevel.Debug,
        VelopackLogLevel.Information => LogLevel.Information,
        VelopackLogLevel.Warning => LogLevel.Warning,
        VelopackLogLevel.Error => LogLevel.Error,
        VelopackLogLevel.Critical => LogLevel.Critical,
        _ => LogLevel.Information,
    };

    [LoggerMessage(EventId = 100, Message = "Velopack: {VelopackMessage}")]
    private static partial void LogVelopack(ILogger logger, LogLevel level, string velopackMessage, Exception? exception);
}
