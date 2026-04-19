# build/

Output directory for Eden's built binaries.

**You don't edit anything here.** This directory is populated by the
build scripts and by CI. Contents are gitignored (they'd bloat the repo
and they're regenerated on every build).

## Produce a build locally

### Windows / PowerShell

```pwsh
scripts/build.ps1                       # host OS, x64
scripts/build.ps1 -Rid linux-x64        # cross-compile
scripts/build.ps1 -Clean                # wipe build/ first
```

### macOS / Linux / bash

```sh
scripts/build.sh                        # host OS, x64
scripts/build.sh linux-x64              # cross-compile
scripts/build.sh --clean                # wipe build/ first
```

## What you get

For a Windows x64 build, the output is:

```
build/
  eden-server-win-x64/
    Eden.Server.Cli.exe   ← double-click or run from terminal
    *.dll                 ← bundled .NET runtime
  eden-viewer-win-x64/
    Eden.Viewer.exe       ← the Godot viewer
    *.dll  *.pck          ← Godot engine + packed assets
```

`Eden.Server.Cli.exe` is **self-contained** — no .NET install required
on the target box. Run it:

```
Eden.Server.Cli.exe              # defaults to port 5001
Eden.Server.Cli.exe 7777         # custom port
```

Env vars:

| var | default | meaning |
|---|---|---|
| `EDEN_LOG_DIR`   | (unset) | if set, writes rolling log files to that directory |
| `EDEN_LOG_LEVEL` | `Information` | one of `Trace`/`Debug`/`Information`/`Warning`/`Error` |

Press **Ctrl+C** to stop; the server shuts down cleanly.

## One-time setup for the viewer export

The server CLI builds with just .NET. The viewer needs **Godot 4.6.1 Mono**
because the viewer IS a Godot project. First time on a given box:

1. Download Godot 4.6.1 Mono from https://godotengine.org/download
2. Either put its directory on `PATH`, or set `GODOT_BIN=<full path to exe>`.
3. Open Godot once → **Editor → Manage Export Templates → Download and
   Install** (one-time; gives Godot the per-platform runtime templates
   it bundles into exported binaries).

After that, `scripts/build.ps1` / `scripts/build.sh` exports the viewer
automatically. If these steps aren't done the scripts skip the viewer
export with a clear message — the server CLI still builds.

## CI builds

Every push to `release/*` or `main` runs the build on all three OSes.
Download the zips from the run page under **Actions → workflow run →
Artifacts**. Those are the same output as `scripts/build.ps1` would
produce locally.
