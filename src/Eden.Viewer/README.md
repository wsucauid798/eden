# Eden.Viewer

The Godot 4 viewer for Eden. This project is **not** part of `Eden.sln` —
open it directly in [Godot 4.3+](https://godotengine.org/) (the .NET build).

## Opening

1. Launch Godot 4 (.NET edition).
2. *Import* → point at `Eden.Viewer/project.godot`.
3. Godot will generate the `.godot/` cache and build the C# assembly.
4. Run. `Scripts/Main.cs` brings Eden up in solo mode, connects, and
   logs world events to Godot's output.
5. Open `Scripts/Main.tscn` to inspect the generated scene in the editor.
   `Main.cs` is a C# tool script, so the 3D viewport previews the code-built
   world while the saved scene stays minimal.
6. When comparing editor vs runtime lighting, disable the viewport's preview
   sun/environment toggles first. Those editor-only helpers can make the
   scene look more realistic than the actual in-game `WorldEnvironment`.

## What lives here

- `project.godot` — Godot project descriptor
- `Eden.Viewer.csproj` — C# project, uses `Godot.NET.Sdk`, references
  `Eden.Shared`, `Eden.Client`, and `Eden.Launcher`
- `Scripts/Main.cs` — root scene script; owns the code-first scene builder,
  editor preview, and runtime bootstrap
- `Scripts/Main.tscn` — the main scene

## Why isn't this in `Eden.sln`?

Godot's `Godot.NET.Sdk` build depends on shims generated when the project
is first opened in the Godot editor. Including this csproj in the main
solution would break `dotnet build` for anyone without Godot installed.
Godot builds and runs this project independently.
