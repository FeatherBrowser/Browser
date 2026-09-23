# Feather Browser

**Version 1.0.0** · Windows · C# / WPF · MIT

Feather is a desktop browser built on Microsoft WebView2, with workspaces, configurable background-tab suspension/unloading, a dark interface and local browser data.

The **Feather WebView3** name refers to Feather's resource-management layer over WebView2. It is not a separate rendering engine or a Microsoft product. Performance improvements have not yet been benchmarked.

## Features

- Workspaces, pinned tabs, session recovery and private windows.
- Dark native menus and built-in home, settings, history, bookmarks and downloads pages.
- Editable quick links and an offline vector landscape.
- Balanced, Memory Saver, Gaming and Responsive profiles.
- Private-memory and CPU readings with a process breakdown.
- Request/cosmetic filtering, site exceptions and permission controls.
- Edge data import and manual autofill for imported passwords protected with Windows DPAPI.

## Build and run

Requirements: Windows, the .NET 9 SDK and Microsoft Edge WebView2 Runtime. Internet access is needed for the initial NuGet restore. Install the **.NET desktop development** workload if using Visual Studio.

From the extracted project folder:

```powershell
dotnet run --project .\src\FeatherBrowser\FeatherBrowser.csproj
```

To build and run the checks:

```powershell
.\scripts\build.ps1
.\scripts\check.ps1
```

The workflow in `.github/workflows/build.yml` builds, checks and packages Windows x64 artifacts. It does not create a public GitHub Release or publish your repository.

## Package a Windows build

```powershell
.\scripts\publish.ps1 -Runtime win-x64 -SelfContained
```

Output: `artifacts/publish/win-x64/`. Distribute the **whole folder** or zip its contents. Self-contained includes .NET, but the user still needs the WebView2 Runtime. Other supported publish targets are `win-arm64` and `win-x86`.

## Data and privacy

Normal browser data lives under `%LOCALAPPDATA%\FeatherBrowser`. Do not commit or share that folder. Private-window WebView2 profiles use temporary folders. Imported passwords use Windows DPAPI; plaintext import CSVs remain your responsibility to remove securely.

Cold unloading restores a tab's URL later and may lose unsaved form/page state. Pin important tabs or add their domains to Keep Alive sites. Pinned/audio/keep-alive/download protections can allow usage above the configured tab budget. Active call/capture detection is not implemented.

The RAM dashboard distinguishes private resident memory from the private-commit fallback. Compare the same processes and metrics in Task Manager. See [resource management](docs/WEBVIEW3.md).

## Development

Read [architecture](docs/ARCHITECTURE.md), [contribution guidance](CONTRIBUTING.md), [security reporting](SECURITY.md) and [validation](docs/VALIDATION.md).

Optional page interaction checks require Python 3, Node.js and Playwright:

```text
npm install --no-save playwright
npx playwright install chromium
python tests/ui/render.py
node tests/ui/check.cjs
```

These checks mock the native bridge. They do not replace Windows WPF/WebView2 testing.

## Release status

The source builds with Windows targeting. Native window controls, popup placement, minimize/restore, tab suspension and imported-password handling still need release testing on Windows. There are no measured RAM/CPU/GPU savings claims, independent security audit, automatic application updater or extension engine in this release.

## License

Feather's source is licensed under [MIT](LICENSE). Third-party packages and the WebView2 Runtime retain their own licenses; see [third-party notices](THIRD_PARTY_NOTICES.md).
