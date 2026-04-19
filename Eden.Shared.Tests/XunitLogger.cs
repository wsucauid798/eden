using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Eden.Shared.Tests;

/// <summary>
/// Pipes <see cref="ILogger"/> output into xUnit's <see cref="ITestOutputHelper"/>
/// so failing tests show the log lines the system emitted on the way down.
/// Swallows writes after the test completes — async continuations sometimes
/// log after the test body returns, and the output helper throws at that point.
/// </summary>
public sealed class XunitLoggerFactory(ITestOutputHelper output) : ILoggerFactory
{
    public void AddProvider(ILoggerProvider provider) { }

    public ILogger CreateLogger(string categoryName) =>
        new XunitLogger(output, categoryName);

    public void Dispose() { }
}

internal sealed class XunitLogger(ITestOutputHelper output, string category) : ILogger
{
    public IDisposable BeginScope<TState>(TState state) where TState : notnull =>
        NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel                       level,
        EventId                        eventId,
        TState                         state,
        Exception?                     exception,
        Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        var line    = $"[{level}] {category}: {message}";
        if (exception is not null)
            line += Environment.NewLine + exception;

        try { output.WriteLine(line); }
        catch { /* test already torn down */ }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
