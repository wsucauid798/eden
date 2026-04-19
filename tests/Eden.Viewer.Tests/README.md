# Eden.Viewer.Tests

xUnit tests for `src/Eden.Viewer/`. Mirrors the source-tests
convention (one test project per source project).

**Build requirement:** needs the **Godot 4 editor** on `PATH` — same as
`Eden.Viewer` itself. This project is **not** part of `eden.sln` for the
same reason: a headless `dotnet build eden.sln` can't build Godot-world
code. Open `src/Eden.Viewer/project.godot` in Godot first; the editor
compiles both projects.

**Scope:** pure C# logic inside the viewer (input binding, settings
parsing, world-state diffing) that can be unit-tested without a running
Godot scene. Integration tests that need an actual Godot `SceneTree`
should use Godot's own test framework (gdUnit4) inside the viewer
project, not xUnit here.

No tests yet — the viewer is still small enough that everything it does
is Godot-bound. This scaffold exists so there's exactly one
`tests/<source>.Tests/` per `src/<source>/`, with a clear place for
non-Godot viewer tests when they arrive.
