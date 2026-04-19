# Eden.Viewer.Tests

Tests for `src/Eden.Viewer/` — real Godot tests of viewer nodes, scenes,
signals, input events, and the wire → scene-graph glue. Runs inside the
Godot runtime via **[gdUnit4](https://github.com/MikeSchulze/gdUnit4)**
(`gdUnit4.api` + `gdUnit4.test.adapter` NuGet packages).

## Project shape

- `Godot.NET.Sdk/4.6.1` project (same Godot/SDK version as `Eden.Viewer`)
- References `Eden.Viewer` so tests can instantiate its scenes and scripts
- `gdUnit4.analyzers` flags tests that touch Godot types without
  `[RequireGodotRuntime]` — red at compile time, not at run time

## Writing a test

```csharp
using GdUnit4;
using static GdUnit4.Assertions;

[TestSuite]
public class PrimRendererTests
{
    [TestCase]
    [RequireGodotRuntime]
    public void PrimUpdate_Creates_MeshInstance()
    {
        // … arrange a scene, drive a PrimUpdate through ViewerClient …
        // AssertThat(scene.GetChildCount()).IsEqual(1);
    }
}
```

- `[TestSuite]` on the class (not xUnit's `[Fact]`)
- `[TestCase]` on methods
- `[RequireGodotRuntime]` whenever the method uses `Godot.*` types
- Assertions via `GdUnit4.Assertions` static imports

## Running tests

### From an IDE (Rider / Visual Studio / VS Code)

The test adapter plugs into VSTest, so tests show up in your IDE's test
explorer alongside the xUnit tests. Run and debug normally.

### From the command line

```sh
dotnet test tests/Eden.Viewer.Tests/Eden.Viewer.Tests.csproj --configuration Release
```

The adapter spins up a headless Godot instance behind the scenes to
execute `[RequireGodotRuntime]` cases. **This needs Godot 4.6.1 on
`PATH`.**

### From inside the Godot editor

Open `src/Eden.Viewer/project.godot` in Godot. The gdUnit4 addon
(installed at `src/Eden.Viewer/addons/gdUnit4/`) gives you a test
inspector panel at the bottom of the editor. Tests that are discovered
there include the ones here via the NuGet adapter.

## Not in `eden.sln`

Same reason `src/Eden.Viewer` isn't: a vanilla `dotnet build eden.sln`
must work on a machine without Godot. The viewer and its tests live on
the Godot-world side of that line.
