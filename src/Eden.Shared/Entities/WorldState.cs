using Eden.Shared.Ids;
using Eden.Shared.Math;

namespace Eden.Shared.Entities;

/// <summary>
/// Authoritative world-wide state. The server owns it, ticks it forward
/// (time-of-day, scripted weather changes, scripted wind changes), and
/// broadcasts updates to every session. Scripts read the current snapshot
/// via <c>IWorldContext.World</c>; viewers mirror it to drive lighting,
/// sky, weather particles, cloth/grass bending.
/// </summary>
public readonly record struct WorldState(
    EdenId<WorldTag> WorldId,
    string           Name,
    float            TimeOfDayHours,   // 0..24, wraps
    Vector3          Wind,             // metres per second
    Weather          Weather,
    float            Gravity);         // m/s² along -Y; Jolt default is 9.81

/// <summary>
/// Broad weather categories. Viewers turn these into particles + sky tint +
/// audio; scripts can read the current category but not drive it directly
/// (future: <c>[OnWeatherChange]</c> event, admin tooling, zone systems).
/// </summary>
public enum Weather : byte
{
    Clear  = 0,
    Cloudy = 1,
    Rain   = 2,
    Snow   = 3,
}

/// <summary>
/// Initial configuration for an <c>EdenServer</c>'s world. Every field has
/// a sensible default so <c>new WorldConfig()</c> gives a working world.
/// </summary>
public readonly record struct WorldConfig(
    string  Name             = "New Eden",
    float   DayLengthSeconds = 24f * 60f,   // 24 real-minutes = 1 game-day (cinematic)
    Vector3 InitialWind      = default,      // (0,0,0)
    Weather InitialWeather   = Weather.Clear,
    float   Gravity          = 9.81f,
    float   StartHoursOfDay  = 8f);          // start at 08:00
