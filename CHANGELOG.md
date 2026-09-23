# Changelog

## 2.0.0

Feather 2.0 is a big update focused on making the browser feel cleaner, faster and more polished.

- Reworked the tab bar so it feels more like a proper browser UI.
- Added website favicons to tabs.
- Moved the new tab button next to the last tab.
- Improved tab overflow so the new tab button stays visible when lots of tabs are open.
- Cleaned up active and inactive tab styling.
- Made the tab bar blend into the navigation bar instead of looking like a separate section.
- Improved tab spacing, close buttons and overall alignment.
- Redesigned the main navigation bar with a much cleaner browser-style layout.
- Reworked the address bar with a rounded design and cleaner spacing.
- Added a proper "Search or enter address" placeholder to the address bar.
- Replaced several old text-based toolbar icons with cleaner vector icons.
- Simplified the main toolbar by moving less important controls out of the way.
- Added cleaner back, forward and reload controls.
- Updated the bookmark button to use a proper icon with active and inactive states.
- Improved the Shield button and moved it into the new toolbar layout.
- Added quick access to downloads from the main toolbar.
- Cleaned up the browser menu button and toolbar alignment.
- Overhauled the New Tab page with a completely new design.
- Added a full-page background to the New Tab page.
- Added quick links to the New Tab page.
- Added a Continue Browsing section using recent history.
- Added New Tab customization controls.
- Improved how internal Feather pages fit with the new browser design.
- Improved tab switching and background tab handling.
- Improved sleeping and waking tabs to reduce unnecessary memory usage.
- Fixed several WebView2 tab lifecycle issues.
- Improved background tab muting.
- Improved favicon loading on sites that update their icon after the page loads.
- Improved workspace tab handling when opening, switching and closing tabs.
- Cleaned up duplicated tab code and simplified parts of the browser internals.
- Improved ad and tracker blocking.
- Improved error handling around navigation and WebView2.
- Fixed several issues caused by the browser UI overhaul.
- General performance, UI and stability improvements.
- Various smaller fixes and code cleanup across the project.

## 1.0.1

- Publish one Windows x64 Setup EXE instead of a ZIP of application files.
- Add per-user installation, Start menu and optional desktop shortcuts, upgrades and uninstall.
- Bundle .NET and a verified Microsoft WebView2 bootstrapper for machines missing the runtime.
- Add a Feather icon to the executable, window, title bar, shortcuts and installer.
- Preserve browser profiles when upgrading or uninstalling.

## 1.0.0

Initial public-source release. Internal preview version numbers have been consolidated into 1.0.0.

- Dark Alpine interface, custom native menus, workspaces and editable home shortcuts.
- Local history, bookmarks, downloads and session recovery.
- Resource profiles, protected background-tab management, CPU and private-memory telemetry.
- Edge imports, site controls, tracker blocking and password-vault integration.
- Centralized application version, corrected About text and navigation-time autofill origin guard.
- Windows build workflow, contribution/security guidance and MIT license.

Known runtime-verification limits are recorded in docs/VALIDATION.md.
