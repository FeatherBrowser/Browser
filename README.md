# Feather Browser

**Version 2.0.0** · Windows · C# / WPF · MIT

Feather is a desktop browser with workspaces, built-in ad and tracker blocking, background-tab freezing, a dark interface and local browser data. It runs on **Feather's own engine layer over open-source Chromium** (CEF via CefSharp). Microsoft Edge, WebView2 and its runtime are not used or required.

The **Feather WebView3** name refers to Feather's tab resource manager. It does not refer to a rendering engine. See [the engine layer](docs/ENGINE.md) for how Feather embeds Chromium and how the engine could be swapped.

## Features

- Workspaces, pinned tabs, session recovery and private windows (in-memory profile, nothing written to disk).
- Background tabs freeze like Chrome's, then unload when memory is tight. Balanced, Memory Saver, Gaming and Responsive profiles.
- Feather Shield request/cosmetic filtering, checked on Chromium's network thread, with site exceptions, tracking-parameter stripping and Do Not Track.
- Site permission bar (camera, microphone, location, notifications, screen sharing, clipboard and more) with per-site memory, plus a site menu in the address bar.
- Find in page, print, zoom, developer tools, themed page context menus, "open link in new tab", mouse back/forward buttons and Chrome-style keyboard shortcuts (listed under Settings → Appearance).
- Tab loading spinners, per-tab audio indicators with click-to-mute, sleeping/unloaded markers.
- Dark native menus and built-in home, settings, history, bookmarks and downloads pages.
- Private-memory and CPU readings with a per-Chromium-process breakdown.
- Edge data import and manual autofill for imported passwords protected with Windows DPAPI.

## Build and run

Requirements: Windows 10 1809 or later (x64), the .NET 9 SDK, and the Visual C++ 2022 x64 runtime (normally already present; published builds carry it). Internet access is needed for the first NuGet restore, which downloads Chromium (well over 100 MB). Install the **.NET desktop development** workload if using Visual Studio.

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

Distribute `artifacts/installer/FeatherBrowser-v3.0.0-win-x64-Setup.exe`. The installer bundles the self-contained .NET app, Chromium and the Visual C++ runtime, adds a Start menu shortcut and optional desktop shortcut, and provides an uninstaller. It needs no internet connection and no administrator rights, and installs for the current user under `%LOCALAPPDATA%\Programs\FeatherBrowser`. Browser profile data stays under `%LOCALAPPDATA%\FeatherBrowser` and is preserved during upgrades and uninstall.

Chromium makes the installer larger than Feather 2.x's. No signing certificate is configured for the app or installer.

Release assets contain the installer EXE. GitHub also supplies its standard source-code archives. Set a new `<Version>` in the project (and corresponding assembly/file versions and changelog) before the next release: existing releases are skipped, not overwritten. Raw folder publishing still supports `win-arm64` and `win-x86`; the installer targets `win-x64` only.

## Keeping Chromium up to date

Chromium ships inside Feather and does not update itself. Update the `CefSharp.Wpf.NETCore` package and release Feather to deliver Chromium security fixes. Dependabot opens these pull requests weekly. See [SECURITY.md](SECURITY.md).

## Data and privacy

Normal browser data lives under `%LOCALAPPDATA%\FeatherBrowser`. Chromium's profile (cookies, cache, site storage) is in `%LOCALAPPDATA%\FeatherBrowser\Chromium`. Do not commit or share that folder. Private windows use an in-memory profile that disappears when the window closes. Imported passwords use Windows DPAPI; plaintext import CSVs remain your responsibility to remove securely.

**Upgrading from 2.x:** favorites, history, settings, sessions and the imported-password vault carry over. Website sign-ins, cookies and site storage lived in WebView2's profile and do not, so you will need to sign in to sites again. The old `%LOCALAPPDATA%\FeatherBrowser\WebView2` folder is no longer used and can be deleted. Chromium's built-in password saving is not available in embedded mode; Feather's imported-password vault and Ctrl+Shift+P autofill still work.

Cold unloading restores a tab's URL later and may lose unsaved form/page state. Sleeping (frozen) tabs keep their state. Pin important tabs or add their domains to Keep Alive sites. Pinned/audio/keep-alive/download protections can allow usage above the configured tab budget. Active call/capture detection is not implemented.

The RAM dashboard distinguishes private resident memory from the private-commit fallback. Compare the same processes and metrics in Task Manager. See [resource management](docs/WEBVIEW3.md).

## Development

Read [architecture](docs/ARCHITECTURE.md), [the engine layer](docs/ENGINE.md), [contribution guidance](CONTRIBUTING.md), [security reporting](SECURITY.md), [changelogs](CHANGELOG.md) and [validation](docs/VALIDATION.md).

Optional page interaction checks require Python 3, Node.js and Playwright:

```text
npm install --no-save playwright
npx playwright install chromium
python tests/ui/render.py
node tests/ui/check.cjs
```

These checks mock the native bridge. They do not replace testing the Windows build itself.
