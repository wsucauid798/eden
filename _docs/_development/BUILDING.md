# Building Eden

## Requirements

* [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (pinned via `global.json`)
* **Godot 4.6.2 Mono** — only if you want to build the viewer. The server
  CLI builds without Godot. Either put the Godot binary on `PATH`, or
  set `GODOT_BIN=<path-to-executable>` for the build scripts.

---

## Produce runnable binaries

```pwsh
scripts/build.ps1                       # host OS, x64
scripts/build.ps1 -Rid linux-x64        # cross-compile
scripts/build.ps1 -Clean                # wipe build/ first
```

```sh
scripts/build.sh                        # host OS
scripts/build.sh linux-x64              # cross-compile
scripts/build.sh --clean
```

Output lands in [`build/`](../../build/README.md):

```
build/
  eden-server-win-x64/
    Eden.Server.Cli.exe   ← the headless server, double-click to run
  eden-viewer-win-x64/
    Eden.Viewer.exe       ← the Godot viewer
```

Both binaries are self-contained — you can copy the directory to
another machine and run it without installing .NET or Godot.

If Godot isn't available, the script still builds the server CLI and
skips the viewer with a clear message. See [build/README.md](../../build/README.md)
for the one-time Godot-setup step (installing export templates).

---

## Develop locally

```sh
dotnet build --configuration Release eden.sln      # build every non-viewer project
dotnet test  --configuration Release eden.sln      # run all xUnit suites
```

Open `eden.sln` in Visual Studio / Rider / VS Code for IDE integration.
The viewer is a separate Godot project: open `src/Eden.Viewer/project.godot`
in Godot 4.6.1 to edit or run from the editor.

Test harnesses exercise the launcher directly via `EdenLauncher.StartSolo()`.
See the per-project test directories under `tests/`.

---

## Layout

```
src/                     — product source (devs work here)
  Eden.Shared/           — domain types + wire protocol
  Eden.Server/           — authoritative sim
  Eden.Server.Cli/       — headless server binary (Main entry point)
  Eden.Client/           — ViewerClient, world-state mirror
  Eden.Launcher/         — StartSolo / StartHostAsync / ConnectAsync + QUIC
  Eden.Logging/          — Serilog composition root
  Eden.Scripting/        — EdenBehavior + event attributes
    Host/                — Eden.Scripting.Host runtime (server-side)
  Eden.Viewer/           — Godot 4 project (not in eden.sln)
tests/                   — test source (devs work here)
  Eden.<project>.Tests/  — one xUnit suite per src/ project
  Eden.Viewer.Tests/     — gdUnit4 suite for the viewer
build/                   — compiled binaries (users run here)
  eden-server-<rid>/     — self-contained server CLI per platform
  eden-viewer-<rid>/     — Godot-exported viewer per platform
scripts/
  build.ps1              — produce build/ contents (PowerShell)
  build.sh               — produce build/ contents (bash)
```
