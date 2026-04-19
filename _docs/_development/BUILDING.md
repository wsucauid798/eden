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

`Eden.Launcher` is a library exposing `EdenLauncher.StartSolo` / `StartHostAsync` / `ConnectAsync` — it has no `Main`. The entry point is [`src/Eden.Viewer/`](src/Eden.Viewer/README.md), a separate Godot 4 project built from the Godot editor. The viewer is not part of `eden.sln`; open `src/Eden.Viewer/project.godot` in Godot to build and run it.

Test harnesses exercise the launcher directly via `EdenLauncher.StartSolo()` etc. — see [`tests/Eden.Tests/`](tests/Eden.Tests/).

---

## Layout

```
src/                     — product projects (one per directory)
  Eden.Shared/           — domain types + wire protocol
  Eden.Server/           — authoritative sim
  Eden.Client/           — ViewerClient, world-state mirror
  Eden.Launcher/         — StartSolo / StartHostAsync / ConnectAsync + QUIC
  Eden.Logging/          — Serilog composition root
  Eden.Scripting/        — EdenBehavior + event attributes (referenced by scripts)
    Host/                — Eden.Scripting.Host runtime (server-side)
  Eden.Viewer/           — Godot 4 project (not in eden.sln)
tests/
  Eden.Tests/            — xUnit suite covering every src/ project
```
