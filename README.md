# Eden

Eden is a platform for creating virtual worlds.

## Build

Requirements:

* [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (pinned via `global.json`)
* On Linux / macOS: `libgdiplus` (e.g. `apt install libgdiplus libc6-dev`)

```sh
dotnet build --configuration Release Eden.sln
```

See [BUILDING.md](BUILDING.md) for configuration and run instructions.

## Licence

[BSD 3-Clause](LICENSE.md).
