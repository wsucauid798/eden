# Building Eden

## Requirements

* [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (pinned via `global.json`)
* **Windows:** Visual Studio 2022/2025 is optional — the CLI is enough.

---

## Build

```sh
dotnet build --configuration Release eden.sln
```

That's it. No prebuild step, no generator — the `.csproj` and `.sln` files are checked in.

Open `eden.sln` in Visual Studio / Rider / VS Code if you prefer an IDE.

---

## Run

`Eden.Launcher` is a library exposing `EdenLauncher.StartSolo` / `StartHostAsync` / `ConnectAsync` — it has no `Main`. The entry point is the [Eden.Viewer](Eden.Viewer/README.md), a separate Godot 4 project built from the Godot editor. `Eden.Viewer` is not part of `eden.sln`; open `Eden.Viewer/project.godot` in Godot to build and run it.

Test harnesses exercise the launcher directly via `EdenLauncher.StartSolo()` etc. — see `Eden.Shared.Tests/`.
