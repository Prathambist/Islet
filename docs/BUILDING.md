# Building and testing

## Requirements

- Windows 10 (version 2004 or later) or Windows 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

Any recent Visual Studio or JetBrains Rider with .NET 10 support works too; open `Islet.sln`.

## Build and run

From the repository root:

```powershell
dotnet build Islet.sln
dotnet run --project src/Islet
```

Only one copy of Islet runs at a time. If one is already running, a new one opens its Settings instead, so quit the installed copy (tray icon → Quit Islet) before running a development build.

## Tests

```powershell
dotnet test
```

The tests are plain xUnit tests and run without a screen. A few touch real Windows features (Restart Manager, file system, cryptography) using temporary files that they clean up.

### Coverage

```powershell
dotnet test --collect:"XPlat Code Coverage" --results-directory ./coverage
dotnet tool install --global dotnet-reportgenerator-globaltool
reportgenerator -reports:"coverage/**/coverage.cobertura.xml" -targetdir:coverage-report -reporttypes:Html
```

Then open `coverage-report/index.html`.

Coverage is high for the logic (timer, island state, shelf, palette, file locks, lyrics, colours, migration, release signatures) and low for the WPF window code, which needs a screen to exercise. When adding a feature, put its decisions in a plain class that can be tested, as `IslandState`, `FileShelf` and `ColorPalette` do, and keep the window code to wiring and drawing.

## Continuous integration

Every push to `main` and every pull request runs [`ci.yml`](../.github/workflows/ci.yml):

1. Build the whole solution with warnings treated as errors
2. Run the tests with coverage, and post a coverage summary on the run
3. Upload the test results and HTML coverage report as an artifact
4. Fail if any NuGet package, direct or transitive, has a known vulnerability

[`codeql.yml`](../.github/workflows/codeql.yml) runs GitHub's CodeQL security analysis on the same events and weekly. Findings appear under the repository's Security → Code scanning.

## Publishing a self-contained build

```powershell
dotnet publish src/Islet/Islet.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish
```

This is what the release workflow does (plus ReadyToRun compilation, a version stamp and signing). For the full release process, see [RELEASING.md](RELEASING.md).
