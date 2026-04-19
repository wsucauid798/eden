# Eden

Eden is a platform for creating single or multiresident virtual worlds.

Pre-alpha.

## Install

Build from source — see [BUILDING](_docs/_development/BUILDING.md).
Prebuilt releases will land on the
[Releases page](https://github.com/wsucauid798/eden/releases) once there's
something worth tagging.

## Run

- **Viewer:** double-click `Eden.Viewer` (or `.exe` / `.app`). Pick
  `1` for solo, `2` to host, `3` to join.
- **Headless server:** `Eden.Server.Cli [port]` — defaults to port 5001,
  Ctrl+C to stop.

## Develop

Requirements: [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
(pinned in `global.json`); [Godot 4.6.2 Mono](https://godotengine.org/download)
only if you're touching the viewer.

```sh
dotnet build --configuration Release eden.sln
dotnet test  --configuration Release eden.sln
```

Layout, prerequisites, and build-script details in
[BUILDING](_docs/_development/BUILDING.md). Test-suite notes in
[TESTING](_docs/_development/TESTING.md).

## Licence

[BSD 3-Clause](LICENSE.md). Copyright © 2026 William Sawyerr.
