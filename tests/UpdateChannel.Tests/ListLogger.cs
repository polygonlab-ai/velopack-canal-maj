using Microsoft.Extensions.Logging;

namespace PolygonLab.UpdateChannel.Tests;

/// <summary>Logger that keeps every entry in memory.</summary>
internal sealed class ListLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, EventId EventId, string Message, Exception? Exception)> Entries { get; } = [];

    public LogLevel MinimumLevel { get; set; } = LogLevel.Trace;

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= MinimumLevel;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (IsEnabled(logLevel))
        {
            Entries.Add((logLevel, eventId, formatter(state, exception), exception));
        }
    }
}
