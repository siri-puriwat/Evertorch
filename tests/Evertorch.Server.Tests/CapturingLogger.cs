using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace Evertorch.Server.Tests
{
internal sealed class CapturingLogger<T> : ILogger<T>
{
    /// <summary>
    ///     Each entry's structured fields are its message template's named values, as a JSON formatter would write them.
    /// </summary>
    public List<(LogLevel Level, EventId EventId, string Message, IReadOnlyDictionary<string, object?> Fields)>
        Entries { get; } = new();

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
        IReadOnlyDictionary<string, object?> fields = state is IEnumerable<KeyValuePair<string, object?>> values
            ? values.ToDictionary(value => value.Key, value => value.Value)
            : new Dictionary<string, object?>();
        lock (Entries)
        {
            Entries.Add((logLevel, eventId, formatter(state, exception), fields));
        }
    }
}
}
