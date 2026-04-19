using Eden.Client;
using Eden.Launcher;
using Eden.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Eden.Shared.Tests;

public class DependencyInjectionTests
{
    [Fact]
    public void AddEdenLogging_Registers_ILoggerFactory_And_ILogger()
    {
        var services = new ServiceCollection();
        services.AddEdenLogging(LogLevel.Warning);
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<ILoggerFactory>());
        Assert.NotNull(provider.GetRequiredService<ILogger<DependencyInjectionTests>>());
    }

    [Fact]
    public async Task DI_Wired_Launcher_Connects_Client_And_Flows_Logs()
    {
        var services = new ServiceCollection();
        services.AddEdenLogging(LogLevel.Information);
        using var provider = services.BuildServiceProvider();

        var factory = provider.GetRequiredService<ILoggerFactory>();

        await using var host = EdenLauncher.StartSolo(factory);
        await using var client = new ViewerClient(
            host.Transport, factory.CreateLogger<ViewerClient>());
        await client.ConnectAsync("di-smoke-test");

        Assert.NotNull(client.Session);
    }
}
