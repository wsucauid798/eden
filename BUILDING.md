# Building Eden

Eden is a fork of OpenSimulator. Until the in-progress modernisation lands, the
build system is inherited from upstream: a Prebuild XML file
([prebuild.xml](prebuild.xml)) generates the solution and project files, and
`dotnet build` compiles them.

---

## Requirements

* [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
* **Linux / macOS only:** `libgdiplus` (e.g. `apt-get install libgdiplus libc6-dev` on Debian/Ubuntu, or `brew install mono-libgdiplus` on macOS)
* **Windows:** Visual Studio 2022 is optional — you can build entirely from the CLI with `dotnet`.

---

## Build

### Windows

```cmd
runprebuild.bat
dotnet build --configuration Release Eden.sln
```

Or open the generated `Eden.sln` in Visual Studio and build the solution.

### Linux / macOS

```sh
./runprebuild.sh
dotnet build --configuration Release Eden.sln
```

Running `runprebuild` is necessary whenever [prebuild.xml](prebuild.xml)
changes; it regenerates the `.sln` and `.csproj` files.

---

## Running

> **Note:** Binaries and config files still carry the inherited `OpenSim`
> names (`OpenSim.exe`, `OpenSim.ini`, etc.) pending the post-demolition
> assembly-name rename. See [_docs/_design/plan.md](_docs/_design/plan.md).

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

---

## References

While the modernisation is in flight, upstream OpenSimulator documentation is
still largely applicable to the build and configuration:

* <http://opensimulator.org/wiki/Build_Instructions>
* <http://opensimulator.org/wiki/Configuration>
