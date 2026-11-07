# Validation — Feather Browser 2.0.0 (Chromium engine)

## Completed in the development environment (Linux, no Windows desktop)

- Full solution compiled with the .NET SDK targeting `net9.0-windows`, against CefSharp 154.0.340's public API (reference assemblies built from the v154.0.340 source tag): **0 warnings, 0 errors**. This includes XAML markup compilation.
- **95 xUnit tests** in FeatherBrowser.Tests (17 new: Chromium process roles, download name safety, non-overwriting download paths; plus lookalike-origin and cross-frame rejection for internal-page messages), **17** FeatherShield tests and **3** FeatherImport tests passed.
- **56 StructureChecks** passed.
- Internal-page interaction checks (`tests/ui`, headless Chromium) passed: quick-link add/edit with `javascript:` and credential-URL rejection, search submission, host-to-page messages, page token on every message, and error-free loading of every internal page at 1320 px and 900 px.
- Injected engine scripts (the `chrome.webview` bridge, audio monitor, mouse navigation) pass `node --check`; the audio monitor's state machine was exercised.
- Internal pages were also rendered from the real C# page builders and inspected visually.

## Windows checks still required before release

The engine layer has not been run interactively. On Windows, check:

1. `dotnet run` from a fresh clone: the window appears, the new tab page loads, sites load, and Task Manager shows FeatherBrowser.exe child processes.
2. Tabs: open, switch, close, reopen (Ctrl+Shift+T), Ctrl+1…9, pin, drag-free reordering via links opened from a page (they appear next to the opener).
3. Sleeping and unloading: leave a tab in the background past the profile's sleep and unload times. Its marker changes to ◐ then ○, it comes back instantly when sleeping and reloads when cold. Repeat with each profile.
4. Audio: play a video, switch away (muted with *Mute background tabs*), click the speaker icon to mute/unmute.
5. Keyboard: every shortcut in Settings → Appearance with focus inside a page, in the address bar and in the find bar. Check that typing in pages is unaffected.
6. Permissions: camera/microphone (e.g. a WebRTC test page), location, notifications; allow, block, dismiss; the address-bar site menu reflects the choice; private windows forget it.
7. Downloads: progress in the status bar, file in Downloads, Library → Downloads, warning when closing a tab mid-download.
8. Popups: a "Sign in with…" popup flow completes; `window.open` from a click opens a tab; unrequested popups are blocked.
9. Find bar, print, zoom (persists per site), DevTools, themed context menu (all items), mouse back/forward buttons.
10. Fullscreen video (F11 and the player's button), Esc to leave; the find and permission bars hide.
11. Command palette (Ctrl+K) over a web page, then close; the page reappears.
12. Crash recovery: open `chrome://crash` in a tab or kill a renderer in Task Manager; the tab recovers.
13. Private window: separate cookies from the main window; nothing remains in `%LOCALAPPDATA%\FeatherBrowser\Chromium` afterwards.
14. Upgrade from 2.x: settings, favorites, history and sessions survive; on the forced 1-tab policy the profile becomes Balanced.
15. `scripts/publish.ps1 -Runtime win-x64 -SelfContained`, then `scripts/installer.ps1` and `scripts/check-installer.ps1`: install offline as a standard user, launch, uninstall.
16. Compare RAM/CPU against 2.x with the scenarios in `docs/WEBVIEW3.md`. No improvement figures are claimed until measured.

The project has not had an independent security audit.

---

# Earlier validation — Feather Browser 1.0.0

## Completed

- Release build with .NET SDK 9.0.318 and Windows targeting: **0 warnings, 0 errors**.
- **53 C# checks passed** for navigation, resource composition, escaping, profile policies, CPU calculations, memory aggregation/fallback and version bindings.
- Headless Chromium interaction checks passed for quick-link editing, unsafe URL rejection, search/toggle/profile messages, telemetry labels and responsive layouts.
- Autofill browser regression passed: a mismatched origin leaves fields empty; the matching synthetic origin fills the synthetic login.
- Native XAML templates compile. Native Windows rendering is not exercised by headless Chromium.
- Source-package scan found no common GitHub/AWS credential patterns, private-key headers or user database/profile exports. This is a limited check, not a security audit.
- Source ZIP integrity verified. Generated output and local browser data are excluded.

## Windows release checks still required

1. Start from a fresh extracted source folder and build/run on Windows.
2. Open the main menu and nested favorites, history, passwords and workspace menus. Verify dark surfaces, keyboard navigation, checkmarks and scrolling.
3. Exercise rapid tab switching, minimize/restore and cold-tab recovery with each profile.
4. Check protected pinned/audio/keep-alive/download tabs.
5. Compare private RAM/commit and group CPU with equivalent Task Manager metrics. No performance improvements are claimed without measurement.
6. Use synthetic data for Edge imports, autofill and private-window checks.
7. Inspect the Windows CI artifact, enable private vulnerability reporting on the repository, and retain all third-party notices when distributing binaries.

The GitHub workflow has been added but has not been run remotely. The project has not had an independent security audit.

## 1.0.1 installer changes

Static checks passed for workflow artifact handoff, project and window XML, release asset naming, and the multi-resolution icon (16 through 256 pixels). The icon was visually inspected.

The editing environment has no Windows runtime, .NET SDK, PowerShell or Inno Setup. Compilation and native setup execution were not performed here. The Windows workflow now compiles the installer, installs it silently into a temporary directory, verifies core installed files/version, and uninstalls it before allowing a release. `scripts/check-installer.ps1` is intended for a disposable CI machine: setup also creates a Start menu shortcut and uninstall registration.

Before distributing publicly, manually check launching the installed browser, icons in Explorer/taskbar, optional desktop shortcut, upgrades over an existing installation, and preservation of browser data. Also exercise WebView2-missing machines with and without internet. The installer and application are unsigned unless your own signing is added.
