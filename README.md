# Feather Browser

**Version 1.0.1** · Windows · C# / WPF · MIT

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

The workflow in `.github/workflows/build.yml` builds and checks the app, then packages a Windows x64 installer. Pushes to `main` or `master` create a GitHub Release for the project version if that release does not already exist. Pull requests only build artifacts.

## Package a Windows build

```powershell
.\scripts\publish.ps1 -Runtime win-x64 -SelfContained
```

Then install [Inno Setup 6.3 or newer](https://jrsoftware.org/isinfo.php) and run:

```powershell
.\scripts\installer.ps1
```

Distribute `artifacts/installer/FeatherBrowser-v1.0.1-win-x64-Setup.exe`. The installer bundles the self-contained .NET app, adds a Start menu shortcut and optional desktop shortcut, and provides an uninstaller. It installs for the current user under `%LOCALAPPDATA%\Programs\FeatherBrowser`. Browser profile data stays under `%LOCALAPPDATA%\FeatherBrowser` and is preserved during upgrades and uninstall.

A Microsoft-signed WebView2 bootstrapper is bundled and runs only if the runtime is missing; in that case installation needs internet access. Packaging also needs internet to download and verify that bootstrapper. See [Microsoft's deployment documentation](https://learn.microsoft.com/microsoft-edge/webview2/concepts/distribution).

The installer is one download; supporting files are unpacked into the install directory. .NET remains included, so this is not a promise of a smaller download. No signing certificate is configured for the app or installer.

Release assets now contain the installer EXE. GitHub also supplies its standard source-code archives. Set a new `<Version>` in the project (and corresponding assembly/file versions and changelog) before the next release: existing releases are skipped, not overwritten. Raw folder publishing still supports `win-arm64` and `win-x86`; the installer targets `win-x64` only.

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
