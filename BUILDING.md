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

The launcher binary lives at `Eden.Launcher/`:

```sh
dotnet run --project Eden.Launcher --configuration Release
```

The [Eden.Viewer](Eden.Viewer/README.md) is a separate Godot 4 project and is built from the Godot editor — it is not part of `eden.sln`.
