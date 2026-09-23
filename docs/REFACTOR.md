# Structure refactor

## Changes

- Moved the application under `src/FeatherBrowser` and updated the solution path.
- Replaced the flat source directory with Domain, Features, Infrastructure and
  Presentation namespaces.
- Split Models.cs into one file per record. Moved credential records out of the
  vault and Windows DPAPI interop into its own platform adapter.
- Split the large MainWindow code-behind by responsibility. Its entry file now
  contains shared fields and constructor wiring.
- Extracted URL/search/internal-library routing into AddressResolver.
- Moved application styles into a merged WPF resource dictionary.
- Extracted internal HTML/CSS/JavaScript and injected scripts into embedded assets.
- Added repository conventions, Windows build/check/publish scripts and CI.
- Removed uploaded bin/obj artifacts from the deliverable; source version remains 1.1.0.

## Validation

The source refactor was checked locally for member preservation, exact internal
page/script reconstruction, project/XAML paths and event-handler references.
See VALIDATION.md for the final results. The full solution compiled successfully
with .NET SDK 9.0.318 targeting Windows on Linux, with zero warnings and errors.
All 29 executable navigation and resource regression checks passed. This environment
cannot run the WPF/WebView2 Windows UI; interactive verification remains outstanding.

## Windows smoke checklist

1. Build the solution and run `scripts/check.ps1`.
2. Launch with an existing profile; confirm bookmarks, history and settings load.
3. Open welcome, new-tab, settings and library pages; exercise their buttons.
4. Open, switch, pin, close and restore tabs; switch and rename workspaces.
5. Exercise background suspension, cold unloading, memory trimming and recovery.
6. Confirm normal/private windows, external URL activation and session restore.
7. Check shield toggles, site permissions, downloads and command palette shortcuts.
8. Import a test Edge profile/password CSV and test manual autofill on a test login.
9. Publish, then launch from the published directory to confirm embedded assets load.

This task changes organization and extracts existing logic. It is not a security
audit, a blocker-engine rewrite, or a completed MVVM migration.
