using Eden.Launcher;
using Eden.Logging;
using Microsoft.Extensions.Logging;

namespace Eden.Server.Cli;

/// <summary>
/// Headless Eden server. Binds QUIC on the given port, logs to Console
/// (and optionally a rolling file), runs until Ctrl+C. No viewer,
/// no Godot — this is the binary you put on a VPS.
/// </summary>
internal static class Program
{
    private const int DefaultPort = 5001;

    public static async Task<int> Main(string[] args)
    {
        var port      = ParsePort(args);
        var logDir    = Environment.GetEnvironmentVariable("EDEN_LOG_DIR");
        var logLevel  = ParseLogLevel(Environment.GetEnvironmentVariable("EDEN_LOG_LEVEL"));

        using var loggerFactory = EdenLoggerFactory.CreateDefault(logLevel, logDir);
        var logger = loggerFactory.CreateLogger(nameof(Eden.Server.Cli));

        logger.LogInformation("Eden headless server starting on port {Port}", port);
        if (logDir is not null)
            logger.LogInformation("Log files: {Dir}", logDir);

        using var shutdown = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;      // don't let Ctrl+C terminate us hard
            shutdown.Cancel();    // cooperative shutdown
            logger.LogInformation("Shutdown requested; stopping server…");
        };

        try
        {
            await using var host = await EdenLauncher.StartHostAsync(
                port, loggerFactory, ct: shutdown.Token);

            logger.LogInformation("Listening on {Endpoint} — press Ctrl+C to stop",
                host.LocalEndPoint);

            // Block until shutdown is requested.
            try { await Task.Delay(Timeout.Infinite, shutdown.Token); }
            catch (OperationCanceledException) { /* expected */ }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Eden server crashed during startup or listen loop");
            return 1;
        }

        logger.LogInformation("Eden server stopped cleanly");
        return 0;
    }

    private static int ParsePort(string[] args) =>
        args.Length > 0 && int.TryParse(args[0], out var p) ? p : DefaultPort;

    private static LogLevel ParseLogLevel(string? env) => env?.ToLowerInvariant() switch
    {
        "trace"       => LogLevel.Trace,
        "debug"       => LogLevel.Debug,
        "warning"     => LogLevel.Warning,
        "error"       => LogLevel.Error,
        _             => LogLevel.Information,
    };
}
