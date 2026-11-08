# Third-party components

The repository's MIT license applies to Feather source, not to Chromium, CEF, CefSharp or other bundled dependencies. Preserve upstream licenses and notices when distributing binaries.

| Component | Package version | Upstream terms/source |
|---|---|---|
| CefSharp.Wpf.NETCore / CefSharp.Common.NETCore | 154.0.340 | BSD-3-Clause; `third-party/CefSharp-LICENSE.txt`; https://github.com/cefsharp/CefSharp |
| Chromium Embedded Framework (chromiumembeddedframework.runtime) | 154.0.34 | BSD-3-Clause; https://bitbucket.org/chromiumembedded/cef |
| Chromium | 154.0.8037.98 | BSD-style plus bundled third-party licenses; full list at `chrome://credits`; `third-party/Chromium-and-CEF-NOTICE.txt` |
| Microsoft Visual C++ runtime (app-local) | 14.x | Redistributable under the Visual Studio license terms; https://learn.microsoft.com/cpp/windows/redistributing-visual-cpp-files |
| Microsoft.Data.Sqlite / Core | 10.0.12 | MIT; https://github.com/dotnet/efcore |
| SQLitePCLRaw | 2.1.x | Apache-2.0; https://github.com/ericsink/SQLitePCL.raw |
| SQLite native library | Brought in by SQLitePCLRaw | See package distribution and https://sqlite.org/copyright.html |
| EasyList and EasyPrivacy filter lists | Snapshot in `external/FeatherFilters/subscriptions` | GPL-3.0 or CC BY-SA 3.0 (dual); https://easylist.to/ |
| uBlock filters and uBlock filters – Quick fixes | Snapshot in `external/FeatherFilters/subscriptions` | GPL-3.0; https://github.com/uBlockOrigin/uAssets |

The filter lists are data files shipped unmodified alongside Feather and loaded at runtime. They stay under their own licenses; keep their header lines (source, license) when updating or redistributing them.

The .NET runtime and native dependencies included in a self-contained publish have their own accompanying notices. Do not strip those files from publish output. The dependency tree can be inspected with `dotnet list src/FeatherBrowser/FeatherBrowser.csproj package --include-transitive`.

The bundled landscape artwork is project artwork. Website names and shortcut branding identify their respective services and do not imply endorsement.

This inventory is not a claim that third-party software is relicensed under MIT. Check notices again when changing dependencies.
