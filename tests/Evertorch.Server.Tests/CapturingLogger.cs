using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Evertorch.Server.Tests
{
internal sealed class CapturingLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, EventId EventId, string Message)> Entries { get; } = new();

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        lock (Entries)
        {
            Entries.Add((logLevel, eventId, formatter(state, exception)));
        }
    }
}
}
