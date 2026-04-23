using Eden.Client;
using Eden.Launcher;
using Eden.Shared.Entities;
using Eden.Shared.World;

namespace Eden.Server.Tests;

/// <summary>
/// The world exists, ticks, and is visible on the wire. Proves:
/// (1) the server owns a WorldState with the configured name and start time,
/// (2) the clock advances TimeOfDayHours as real time passes,
/// (3) the initial WorldState arrives on ClientHello,
/// (4) later clock ticks reach the viewer via broadcast.
/// </summary>
public class WorldStateTests
{
    [Fact]
    public async Task Server_Exposes_Initial_World_From_Config()
    {
        var cfg = new WorldConfig(
            Name:            "Testland",
            StartHoursOfDay: 12f,
            InitialWeather:  Weather.Cloudy);
        await using var host = EdenLauncher.StartSolo(worldConfig: cfg);

        var state = host.Server.WorldSnapshot;
        Assert.Equal("Testland",      state.Name);
        Assert.Equal(Weather.Cloudy,  state.Weather);
        Assert.Equal(TerrainKind.Flat, state.Terrain.Kind);
        Assert.Equal(EnvironmentProfile.Cloudy, state.Environment.Profile);
        Assert.Equal(EnvironmentState.FromWeather(Weather.Cloudy).CloudCover, state.Environment.CloudCover);
        // TimeOfDayHours may have ticked once by the time we look — allow a small delta.
        Assert.InRange(state.TimeOfDayHours, 12f, 12.5f);
    }

    [Fact]
    public async Task World_Clock_Advances_Time_Of_Day()
    {
        // 4-second day: 24 game-hours per 4 real-seconds = 6 game-hours / real-second.
        // After 1s of real time we expect TimeOfDayHours to have moved by ~6 hours.
        var cfg = new WorldConfig(DayLengthSeconds: 4f, StartHoursOfDay: 0f);
        await using var host = EdenLauncher.StartSolo(worldConfig: cfg);

        var initial = host.Server.WorldSnapshot.TimeOfDayHours;
        await Task.Delay(TimeSpan.FromSeconds(1.2));
        var later = host.Server.WorldSnapshot.TimeOfDayHours;

        Assert.True(later > initial + 2f,
            $"Expected time to advance by at least 2h in 1.2s at 6h/s; got {later - initial:F2}h");
    }

    [Fact]
    public async Task Viewer_Receives_Initial_World_State_On_Hello()
    {
        var cfg = new WorldConfig(Name: "Arrival World", InitialWeather: Weather.Rain);
        await using var host = EdenLauncher.StartSolo(worldConfig: cfg);

        await using var alice = new ViewerClient(host.Transport);
        await alice.ConnectAsync("Alice");

        var seen = await WaitForAsync(() => alice.RemoteWorldState is not null, TimeSpan.FromSeconds(1));
        Assert.True(seen, "Viewer never received initial WorldStateUpdate.");
        Assert.Equal("Arrival World", alice.RemoteWorldState!.Value.Name);
        Assert.Equal(Weather.Rain,    alice.RemoteWorldState.Value.Weather);
        Assert.Equal(EnvironmentProfile.Rain, alice.RemoteWorldState.Value.Environment.Profile);
        Assert.Equal(TerrainKind.Flat, alice.RemoteWorldState.Value.Terrain.Kind);
    }

    [Fact]
    public async Task Viewer_Receives_Subsequent_World_Clock_Ticks()
    {
        var cfg = new WorldConfig(DayLengthSeconds: 4f, StartHoursOfDay: 0f);
        await using var host = EdenLauncher.StartSolo(worldConfig: cfg);

        await using var alice = new ViewerClient(host.Transport);
        await alice.ConnectAsync("Alice");

        // Wait for the first broadcast.
        Assert.True(await WaitForAsync(
            () => alice.RemoteWorldState is not null, TimeSpan.FromSeconds(1)));
        var initial = alice.RemoteWorldState!.Value.TimeOfDayHours;

        // Wait for the clock to advance materially (>= 1 game hour).
        var advanced = await WaitForAsync(
            () => alice.RemoteWorldState!.Value.TimeOfDayHours > initial + 1f,
            TimeSpan.FromSeconds(2));
        Assert.True(advanced, "Viewer did not receive advancing world-state ticks.");
    }

    private static async Task<bool> WaitForAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate()) return true;
            await Task.Delay(10);
        }
        return predicate();
    }
}
