using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChapterCreator.Tests;

/// <summary>
/// An in-memory <see cref="ILogger{T}"/> implementation that captures log entries for assertion in tests.
/// </summary>
/// <typeparam name="T">The category type for the logger.</typeparam>
internal sealed class ListLogger<T> : ILogger<T>
{
    /// <summary>
    /// Gets the captured log entries.
    /// </summary>
    public List<(LogLevel LogLevel, string Message, Exception? Exception)> Entries { get; } = [];

    /// <inheritdoc />
    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull
    {
        return NullScope.Instance;
    }

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    /// <inheritdoc />
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Entries.Add((logLevel, formatter(state, exception), exception));
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
