using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;

namespace Eden.Logging;

/// <summary>
/// Composition-root helper for building an <see cref="ILoggerFactory"/> backed
/// by Serilog. Library projects stay on <c>Microsoft.Extensions.Logging.Abstractions</c>
/// and receive the factory from whichever app hosts them (the Godot viewer, a
/// CLI harness, or the test adapter). Swap the provider here and nothing else
/// in the codebase needs to change.
/// </summary>
public static class EdenLoggerFactory
{
    /// <summary>
    /// Build a logger factory with a console sink and, optionally, a
    /// day-rolling file sink.
    /// </summary>
    /// <param name="minimum">Minimum level emitted by any sink.</param>
    /// <param name="fileDir">If non-null, write <c>eden-YYYY-MM-DD.log</c>
    /// files into this directory. Created if missing.</param>
    public static ILoggerFactory CreateDefault(
        LogLevel minimum = LogLevel.Information,
        string?  fileDir = null)
    {
        var config = new LoggerConfiguration()
            .MinimumLevel.Is(ToSerilog(minimum))
            .Enrich.FromLogContext()
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}");

        if (fileDir is not null)
        {
            Directory.CreateDirectory(fileDir);
            config = config.WriteTo.File(
                path:            Path.Combine(fileDir, "eden-.log"),
                rollingInterval: RollingInterval.Day,
                outputTemplate:  "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}");
        }

        return new SerilogLoggerFactory(config.CreateLogger(), dispose: true);
    }

    private static LogEventLevel ToSerilog(LogLevel level) => level switch
    {
        LogLevel.Trace       => LogEventLevel.Verbose,
        LogLevel.Debug       => LogEventLevel.Debug,
        LogLevel.Information => LogEventLevel.Information,
        LogLevel.Warning     => LogEventLevel.Warning,
        LogLevel.Error       => LogEventLevel.Error,
        LogLevel.Critical    => LogEventLevel.Fatal,
        _                    => LogEventLevel.Information,
    };
}
