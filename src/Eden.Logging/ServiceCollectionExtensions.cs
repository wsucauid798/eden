using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Eden.Logging;

/// <summary>
/// DI wiring for Eden's logging stack. Library consumers at a composition
/// root (the Godot viewer, a future CLI host, a test) call
/// <see cref="AddEdenLogging"/> on their <see cref="IServiceCollection"/>
/// and then resolve <see cref="ILogger{T}"/> normally.
/// </summary>
public static class EdenLoggingServiceCollectionExtensions
{
    /// <summary>
    /// Register an <see cref="ILoggerFactory"/> backed by Serilog (console +
    /// optional day-rolling file). Also registers the standard
    /// <see cref="ILogger{T}"/> bindings so constructor injection works.
    /// </summary>
    public static IServiceCollection AddEdenLogging(
        this IServiceCollection services,
        LogLevel                minimum = LogLevel.Information,
        string?                 fileDir = null)
    {
        var factory = EdenLoggerFactory.CreateDefault(minimum, fileDir);
        services.AddSingleton(factory);
        services.AddLogging(builder => builder.AddProvider(new LoggerFactoryProvider(factory)));
        return services;
    }

    // Bridges an already-built ILoggerFactory into the ILoggerProvider shape
    // that AddLogging's builder expects. Lets us keep EdenLoggerFactory as the
    // single source of truth for sink configuration.
    private sealed class LoggerFactoryProvider(ILoggerFactory factory) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => factory.CreateLogger(categoryName);
        public void Dispose() { /* factory owned by the DI container */ }
    }
}
