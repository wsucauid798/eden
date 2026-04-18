# Eden

Eden is a platform for creating virtual worlds.

## Build

Requirements:

* [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
* On Linux / macOS: `libgdiplus` (e.g. `apt install libgdiplus libc6-dev`)

Generate the projects, then build:

```sh
# Linux / macOS
./runprebuild.sh
dotnet build --configuration Release Eden.sln

# Windows
runprebuild.bat
dotnet build --configuration Release Eden.sln
```

See [BUILDING.md](BUILDING.md) for configuration and run instructions.

## Licence

[BSD 3-Clause](LICENSE.md).
