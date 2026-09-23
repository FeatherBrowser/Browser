# Validation — Feather Browser 1.0.0

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
