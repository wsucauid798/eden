# Eden

Eden is a cross-platform application for creating, hosting, and visiting
virtual worlds. One binary per OS. Double-click, pick a mode, step into
a world — no separate server or viewer install, no database setup, no
configuration files.

The north star: **someone who has never used a virtual world should be
standing in one in under a minute.**

Today a world is a terrain with physics, scriptable objects, shared
chat, and a day/night cycle. Behaviour in-world is written in **C#** —
compiled classes deriving from `EdenBehavior` with event-attribute
handlers (`[OnTouch]`, `[OnChat]`, `[OnTimer]`, …). Networking is QUIC
over `System.Net.Quic`; the viewer runs on Godot 4.

Runs on Windows, Linux, and macOS.

---

## Install

### Prebuilt binary (recommended)

CI produces a binary on every push. Grab the latest from the
[GitHub Actions artifacts](https://github.com/wsucauid798/eden/actions).
Download the zip for your platform, extract, and run:

| Platform | Binary |
|---|---|
| Windows | `eden-viewer-win-x64/Eden.Viewer.exe` |
| Linux   | `eden-viewer-linux-x64/Eden.Viewer` |
| macOS   | `eden-viewer-osx-arm64/Eden.Viewer.app` |

The headless server ships alongside: `eden-server-<platform>/Eden.Server.Cli[.exe]`.

### Build from source

See [_docs/_development/BUILDING.md](_docs/_development/BUILDING.md).
Short version:

```pwsh
scripts/build.ps1          # Windows / PowerShell
```
```sh
scripts/build.sh           # macOS / Linux
```

Binaries land in [`build/`](build/README.md).

---

## Run

### The viewer

Double-click `Eden.Viewer` (or `Eden.Viewer.exe`). Pick a mode:

- **1 — Solo.** Server runs in-process. No socket. Offline; just you.
- **2 — Host.** Opens a QUIC listener on `localhost:5001`. Share your
  IP or tunnel it to let friends join.
- **3 — Join.** Connects to `localhost:5001` by default; set
  `EDEN_HOST` / `EDEN_PORT` for a remote server.

Controls inside a world:
- **WASD** — move relative to camera
- **Mouse** — orbit the camera
- **Esc** — release / recapture the mouse cursor

### The headless server

For a dedicated server (no viewer window):

```
Eden.Server.Cli              # defaults to port 5001
Eden.Server.Cli 7777         # custom port
```

Environment variables:

| Var | Default | Meaning |
|---|---|---|
| `EDEN_LOG_DIR`   | (unset) | Write rolling log files to this directory. |
| `EDEN_LOG_LEVEL` | `Information` | `Trace` / `Debug` / `Information` / `Warning` / `Error`. |

Ctrl+C shuts the server down cleanly.

---

## Develop

Requirements:
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (version pinned via [global.json](global.json))
- [Godot 4.6.2 Mono](https://godotengine.org/download) — only if you want
  to build or run the viewer

Get going:

```sh
dotnet build --configuration Release eden.sln       # build everything except the viewer
dotnet test  --configuration Release eden.sln       # run every xUnit suite
```

The viewer is a separate Godot project at `src/Eden.Viewer/`; open
`src/Eden.Viewer/project.godot` in Godot 4.6.2 to edit and run from the
editor, or produce the binary via the build script.

### Where things live

```
src/                     ← source (devs)
  Eden.Shared/           — domain types + wire protocol
  Eden.Server/           — authoritative sim
  Eden.Server.Cli/       — headless server executable
  Eden.Client/           — ViewerClient, world-state mirror
  Eden.Launcher/         — StartSolo / StartHostAsync / ConnectAsync + QUIC
  Eden.Logging/          — Serilog composition root
  Eden.Scripting/        — EdenBehavior + event attributes
    Host/                — Eden.Scripting.Host runtime
  Eden.Viewer/           — Godot 4 C# project
tests/                   ← tests (devs)
  Eden.<project>.Tests/  — xUnit per src/ project
  Eden.Viewer.Tests/     — gdUnit4 (Godot-runtime tests)
build/                   ← binaries (users)
scripts/                 ← build + automation
```

Docs:
- [BUILDING.md](_docs/_development/BUILDING.md) — build + install
  prerequisites + repo layout
- [TESTING.md](_docs/_development/TESTING.md) — running the test suites

---

## Licence

[BSD 3-Clause](LICENSE.md).

Copyright © 2026 William Sawyerr.
