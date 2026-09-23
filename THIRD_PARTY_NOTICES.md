# Third-party components

The repository's MIT license applies to Feather source, not to Microsoft WebView2 or other bundled dependencies. Preserve upstream licenses and notices when distributing binaries.

| Component | Package version | Upstream terms/source |
|---|---|---|
| Microsoft.Web.WebView2 SDK | 1.0.4191.47 | Microsoft package license, included in `third-party/WebView2-LICENSE.txt` |
| Microsoft.Data.Sqlite / Core | 9.0.20 | MIT; https://github.com/dotnet/efcore |
| SQLitePCLRaw | 2.1.12 | Apache-2.0; https://github.com/ericsink/SQLitePCL.raw |
| SQLite native library | Brought in by SQLitePCLRaw | See package distribution and https://sqlite.org/copyright.html |

WebView2 Runtime is a separately installed Microsoft runtime. The .NET runtime and native dependencies included in a self-contained publish have their own accompanying notices. Do not strip those files from publish output. The dependency tree can be inspected with `dotnet list src/FeatherBrowser/FeatherBrowser.csproj package --include-transitive`.

The bundled landscape SVG is project artwork. Website names and shortcut branding identify their respective services and do not imply endorsement.

This inventory is not a claim that third-party software is relicensed under MIT. Check notices again when changing dependencies.
