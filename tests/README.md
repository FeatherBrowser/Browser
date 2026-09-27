# Tests

Run the unit suite with the .NET 9 SDK:

```sh
dotnet test tests/FeatherBrowser.Tests/FeatherBrowser.Tests.csproj --configuration Release
```

The xUnit suite covers navigation and route rejection, complete performance profiles and transitions, grace-period boundaries, CPU normalization, partial memory samples, embedded page composition, HTML/script escaping, and settings serialization. Tests use fixed timestamps and do not launch a browser or access websites.

The project links the actual platform-independent production source and embeds the real page assets, following the existing StructureChecks approach. This allows Windows and Linux runs without loading WPF. It does not replace building the Windows application or test WebView2 rendering, native dialogs, or interactive fullscreen behavior.

Initialize dependencies with `git submodule update --init --recursive`. Run the existing broader smoke checks with `dotnet run --project tests/FeatherBrowser.StructureChecks --configuration Release`.

The Tests workflow runs on pushes and pull requests and supports manual runs. Each OS uploads TRX test results and Cobertura coverage under its test-results artifact, including on failure. The Windows build/release workflow calls the same test workflow and waits for success before building and releasing. Coverage is reported without an arbitrary minimum threshold.
