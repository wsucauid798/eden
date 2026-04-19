# Testing

Eden uses [xUnit](https://xunit.net/) for automated tests, driven by the
`dotnet test` CLI. No separate runner install is required — xUnit is pulled
in as a NuGet dependency of the test projects.

## Running the tests

Build first, then run the tests:

```sh
dotnet build --configuration Release eden.sln
```

Then:

```sh
dotnet test --configuration Release --no-build eden.sln
```

Useful options:

```sh
# Run tests in a single project
dotnet test tests/Eden.Tests/Eden.Tests.csproj

# Run a single test by name pattern
dotnet test --filter "FullyQualifiedName~ViewerClient"

# Verbose output
dotnet test --logger "console;verbosity=detailed"
```

## Layout

- Product code lives under `src/` (one project per directory).
- Test code lives under `tests/` — currently one project, `Eden.Tests/`,
  covering every `src/` project.
- If a single test project becomes unwieldy, split it by the source project
  it targets (`tests/Eden.Scripting.Tests/`, etc.). Today's scale doesn't
  justify the split.
- New test projects need to be added to [eden.sln](eden.sln) so `dotnet build`
  / `dotnet test` pick them up.

## IDE integration

- **Visual Studio 2022 / Rider** — both detect xUnit tests automatically once
  the solution is built. Right-click a test or test class to run or debug.
- **VS Code** — install the
  [C# Dev Kit](https://marketplace.visualstudio.com/items?itemName=ms-dotnettools.csdevkit)
  extension; the Test Explorer will discover xUnit tests.

## Continuous integration

CI runs on every push and pull request against `main` via
[.github/workflows/ci.yml](.github/workflows/ci.yml). It builds on both
Ubuntu and Windows and runs the full test suite.
