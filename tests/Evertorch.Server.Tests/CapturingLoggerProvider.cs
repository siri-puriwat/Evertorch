using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace Evertorch.Server.Tests
{
/// <summary>
///     Every line a host logs, whatever its category, with the formatted message and the structured field values, as
///     a sink would receive them.
/// </summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly List<string> m_lines = new();

    /// <summary>
    ///     Each entry's category, message, and field values in one line of text.
    /// </summary>
    public IReadOnlyList<string> Lines
    {
        get
        {
            lock (m_lines)
            {
                return m_lines.ToArray();
            }
        }
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new Logger(this, categoryName);
    }

    public void Dispose()
    {
    }

    private void Add(string line)
    {
        lock (m_lines)
        {
            m_lines.Add(line);
        }
    }

    private sealed class Logger : ILogger
    {
        private readonly CapturingLoggerProvider m_provider;
        private readonly string m_category;

        public Logger(CapturingLoggerProvider provider, string category)
        {
            m_provider = provider;
            m_category = category;
        }

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
            string fields = state is IEnumerable<KeyValuePair<string, object?>> values
                ? string.Join(" ", values.Select(value => $"{value.Key}={value.Value}"))
                : string.Empty;
            m_provider.Add(
                $"{m_category} {logLevel} {eventId.Name}: {formatter(state, exception)} {fields} {exception}");
        }
    }
}
}
