# Building Eden

## Requirements

* [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (pinned via `global.json`)
* **Linux / macOS only:** `libgdiplus` (e.g. `apt-get install libgdiplus libc6-dev` on Debian/Ubuntu, or `brew install mono-libgdiplus` on macOS)
* **Windows:** Visual Studio 2022/2025 is optional — the CLI is enough.

---

## Build

```sh
dotnet build --configuration Release Eden.sln
```

That's it. No prebuild step, no generator — the `.csproj` and `.sln` files are checked in.

### One-time: trust the dev certificate

`Eden.Server` listens over HTTPS (required for HTTP/3 / WebTransport). Trust
the ASP.NET Core dev cert once per machine:

```sh
dotnet dev-certs https --trust
```

Open `Eden.sln` in Visual Studio / Rider / VS Code if you prefer an IDE.

---

## Running

> **Note:** Binaries and config files still carry the inherited `OpenSim`
> names (`OpenSim.exe`, `OpenSim.ini`, etc.) pending the post-Linden-cut
> rename. See [_docs/_development/backlog.md](_docs/_development/backlog.md).

From the `bin/` folder:

* **Windows:** `OpenSim.exe`
* **Linux / macOS:** `./opensim.sh`

---

## Configure

### Standalone mode

1. Copy `bin/OpenSim.ini.example` → `bin/OpenSim.ini`.
2. Verify the `[Const]` section and adjust for your environment.
3. In `[Architecture]`, uncomment either `Standalone.ini` (no Hypergrid) or
   `StandaloneHypergrid.ini`.
4. Copy `bin/config-include/StandaloneCommon.ini.example`
   → `bin/config-include/StandaloneCommon.ini`.

The `StandaloneCommon.ini` file describes the database and backend services.
It defaults to SQLite, which needs no additional setup.

### Grid mode

1. Copy `bin/OpenSim.ini.example` → `bin/OpenSim.ini` and edit the `[Const]`
   section.
2. In `[Architecture]`, uncomment either `Grid.ini` (no Hypergrid) or
   `GridHypergrid.ini`.
3. Copy `bin/config-include/GridCommon.ini.example`
   → `bin/config-include/GridCommon.ini` and edit as needed.

Each grid has its own specific requirements — follow your grid's instructions
where they diverge from the defaults.
