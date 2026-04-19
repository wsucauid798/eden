using Eden.Client;
using Eden.Launcher;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Eden.Logging.Tests;

public class XunitLoggerTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Launcher_Logs_Flow_Through_Xunit_Adapter()
    {
        var factory = new XunitLoggerFactory(output);
        await using var host = EdenLauncher.StartSolo(factory);

        await using var client = new ViewerClient(
            host.Transport, factory.CreateLogger<ViewerClient>());
        await client.ConnectAsync("adapter-smoke-test");

        // If we got here without the adapter throwing, the pipe works. The
        // user will see "Session … joined" + "Connected as user …" in the
        // test output for this test when it's run with detailed logging.
        Assert.NotNull(client.Session);
    }
}
