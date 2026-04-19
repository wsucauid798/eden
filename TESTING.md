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
dotnet test Eden.Shared.Tests/Eden.Shared.Tests.csproj

# Run a single test by name pattern
dotnet test --filter "FullyQualifiedName~ViewerClient"

# Verbose output
dotnet test --logger "console;verbosity=detailed"
```

## Adding tests

- Tests do not belong in production assemblies. Put them in a parallel project
  named after the assembly under test, suffixed with `.Tests` — e.g.
  `Eden.Shared.Tests` alongside `Eden.Shared`.
- If you add a new test project, add it to [eden.sln](eden.sln) so it gets
  picked up by `dotnet build` / `dotnet test`.

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
