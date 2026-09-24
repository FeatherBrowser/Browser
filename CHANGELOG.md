# Changelog

## 2.0.0

Feather 2.0 is a big update focused on making the browser feel cleaner, faster and more polished.

- Reworked the tab bar with cleaner spacing, close buttons and alignment.
- Added website favicons to tabs.
- Moved the new tab button next to the last tab.
- Improved tab overflow to keep the new tab button accessible.
- Cleaned up active and inactive tab styling.
- Made the tab bar blend into the navigation bar.
- Redesigned the main navigation bar with a cleaner browser-style layout.
- Reworked the address bar with a rounded design and improved spacing.
- Added a “Search or enter address” placeholder.
- Replaced several text-based toolbar icons with vector icons.
- Simplified the toolbar by moving less important controls out of the way.
- Added cleaner back, forward and reload controls.
- Updated the bookmark button with active and inactive icon states.
- Improved the Shield button and its placement.
- Added quick access to downloads from the toolbar.
- Cleaned up the browser menu button and toolbar alignment.
- Overhauled the New Tab page with a new design and full-page background.
- Added customizable quick links.
- Added a Continue Browsing section using recent history.
- Added New Tab customization controls.
- Improved how internal Feather pages fit with the browser design.
- Improved command palette sizing for smaller windows.
- Improved keyboard focus indicators and accessibility labels.
- Prevented status text from overlapping resource information.
- Removed unnecessary sidebar filler.
- Improved tab switching and background tab handling.
- Improved sleeping and waking tabs to reduce unnecessary memory usage.
- Fixed several WebView2 tab lifecycle issues.
- Improved background tab muting.
- Improved favicon loading on sites that update their icon after loading.
- Fixed middle-click tab closing.
- Updated tab-strip sizing when tabs are closed.
- Improved workspace tab handling when opening, switching and closing tabs.
- Restored missing tab suspension, unloading and memory-trimming methods.
- Added cancellation checks for scheduled background-tab work.
- Added protection checks for pinned tabs, active downloads, keep-alive sites and audio tabs when audio protection is enabled.
- Split tab creation, lifecycle, header visuals and favicon handling into focused files.
- Reused frozen brushes and fallback icon graphics across tabs.
- Consolidated repeated styles, colours and icon geometry.
- Replaced numbered colour resources with descriptive names.
- Removed unused resources, redundant layout wrappers and duplicated code.
- Improved XAML formatting and separated command palette templates from its layout.
- Updated structure checks to match the New Tab page’s current data bindings.
- Fixed malformed JavaScript in quick-link rendering that prevented the New Tab script from running.
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
