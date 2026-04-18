# Testing

Eden uses [NUnit 3](https://nunit.org/) for automated tests, driven by the
modern `dotnet test` CLI. No separate NUnit install is required — the runner
is pulled in as a NuGet dependency of the test projects.

## Running the tests

First, generate the solution and project files, then build:

```sh
# Linux / macOS
./runprebuild.sh
dotnet build --configuration Release Eden.sln

# Windows
runprebuild.bat
dotnet build --configuration Release Eden.sln
```

Then run the tests:

```sh
dotnet test --configuration Release --no-build Eden.sln
```

Useful options:

```sh
# Run tests in a single project
dotnet test Eden/Framework/Tests/OpenSim.Framework.Tests.csproj

# Run a single test by name pattern
dotnet test --filter "FullyQualifiedName~Util.Escape"

# Verbose output
dotnet test --logger "console;verbosity=detailed"
```

## Adding tests

- Tests do not belong in production assemblies. Put them in a parallel project
  named after the assembly under test, suffixed with `.Tests` — e.g.
  `OpenSim.Framework.Tests` alongside `OpenSim.Framework`. (Assembly names
  still carry the inherited `OpenSim` prefix until the post-demolition
  rename.)
- Keep tests close to the code they exercise: a `Tests/` sub-directory next to
  the source is the convention.
- If you add a new test project, it must be listed in [prebuild.xml](prebuild.xml)
  so that `runprebuild` picks it up.

## IDE integration

- **Visual Studio 2022 / Rider** — both detect NUnit tests automatically once
  the solution is built. Right-click a test or test class to run or debug.
- **VS Code** — install the
  [C# Dev Kit](https://marketplace.visualstudio.com/items?itemName=ms-dotnettools.csdevkit)
  extension; the Test Explorer will discover NUnit tests.

## Continuous integration

CI runs on every push and pull request against `main` via
[.github/workflows/ci.yml](.github/workflows/ci.yml). It builds on both
Ubuntu and Windows and runs the full test suite.

## Data-layer tests

Tests that exercise database backends read connection settings from
[Eden/Data/Tests/Resources/TestDataConnections.ini](Eden/Data/Tests/Resources/TestDataConnections.ini).
Copy the `.example` variant and point it at a local MySQL / PostgreSQL
instance if you want to run those suites; the SQLite backend works with no
additional setup.
