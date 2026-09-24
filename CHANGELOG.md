# Changelog

**## 2.0.0**

Feather 2.0 is a major update focused on making the browser cleaner, faster, more reliable and more polished.

### Added

\- Added website favicons to tabs.  
\- Added a “Search or enter address” placeholder to the address bar.  
\- Added quick access to downloads from the toolbar.  
\- Added customizable quick links to the New Tab page.  
\- Added a Continue Browsing section using recent history.  
\- Added New Tab customization controls.  
\- Added cancellation checks for scheduled background-tab work.  
\- Added protection checks for pinned tabs, active downloads, keep-alive sites and audio tabs when audio protection is enabled.  
\- Added safer recovery from corrupted or incomplete browser data files.  
\- Added improved backup and temporary-file handling when saving browser data.  
\- Added validation for restored session tabs.  
\- Added improved URL validation across bookmarks, history and favicon handling.  
\- Added more specific error handling for JSON parsing and browser data file access.  
\- Added reusable helper methods for repeated browser logic.  
\- Added named constants to replace repeated magic values across browser systems.  
\- Added stronger null handling and argument validation across internal services.  

### Changed

\- Reworked the tab bar with cleaner spacing, close buttons and alignment.  
\- Moved the new tab button next to the last tab.  
\- Improved tab overflow to keep the new tab button accessible.  
\- Cleaned up active and inactive tab styling.  
\- Made the tab bar blend into the navigation bar.  
\- Redesigned the main navigation bar with a cleaner browser-style layout.  
\- Reworked the address bar with a rounded design and improved spacing.  
\- Replaced several text-based toolbar icons with vector icons.  
\- Simplified the toolbar by moving less important controls out of the way.  
\- Improved back, forward and reload controls.  
\- Updated the bookmark button with active and inactive icon states.  
\- Improved the Shield button and its placement.  
\- Cleaned up the browser menu button and toolbar alignment.  
\- Overhauled the New Tab page with a new design and full-page background.  
\- Improved how internal Feather pages fit with the browser design.  
\- Improved Library page data handling and JSON generation.  
\- Improved command palette sizing for smaller windows.  
\- Improved keyboard focus indicators and accessibility labels.  
\- Improved tab switching and background tab handling.  
\- Improved sleeping and waking tabs to reduce unnecessary memory usage.  
\- Improved background tab muting.  
\- Improved favicon loading on sites that update their icon after loading.  
\- Improved favicon validation, caching and storage handling.  
\- Improved workspace tab handling when opening, switching and closing tabs.  
\- Improved workspace validation and normalization when loading settings.  
\- Improved browser data storage and persistence reliability.  
\- Improved settings migration handling with cleaner versioned migration steps.  
\- Improved session recovery and legacy session compatibility.  
\- Improved bookmark import and duplicate detection.  
\- Improved history merging and duplicate URL handling.  
\- Improved history trimming and storage limits.  
\- Improved download history management and cleanup.  
\- Improved ad and tracker blocking.  
\- Improved error handling around navigation and WebView2.  
\- Improved XAML formatting and project structure.  
\- Split tab creation, lifecycle, header visuals and favicon handling into focused files.  
\- Simplified workspace code by reducing repeated conditions and duplicated workspace comparisons.  
\- Reduced repeated tab-list scans when updating the workspace sidebar.  
\- Simplified several loops, conditions and event handlers.  
\- Reused frozen brushes and fallback icon graphics across tabs.  
\- Consolidated repeated styles, colours and icon geometry.  
\- Replaced numbered colour resources with descriptive names.  
\- Updated structure checks to match the New Tab page’s current data bindings.  
\- Improved general performance, UI responsiveness, reliability and stability.  

### Fixed

\- Fixed several WebView2 tab lifecycle issues.  
\- Fixed middle-click tab closing.  
\- Fixed tab-strip sizing after tabs are closed.  
\- Fixed missing tab suspension, unloading and memory-trimming methods.  
\- Fixed status text overlapping resource information.  
\- Fixed session data being unnecessarily loaded multiple times.  
\- Fixed duplicate and invalid workspace entries being retained.  
\- Fixed invalid or missing active workspaces after loading settings.  
\- Fixed malformed or duplicate history entries potentially causing import failures.  
\- Fixed unnecessary browser-data writes in several bookmark and download operations.  
\- Fixed broad exception handling hiding expected persistence errors.  
\- Fixed malformed JavaScript in quick-link rendering that prevented the New Tab script from running.  
\- Fixed several issues introduced during the browser UI overhaul.  
\- Fixed various smaller stability and code-quality issues across the project.  

### Removed

\- Removed unnecessary sidebar filler.  
\- Removed unused resources.  
\- Removed redundant layout wrappers.  
\- Removed duplicated tab and workspace logic.  
\- Removed unnecessary asynchronous event handlers.  
\- Removed redundant `Task.CompletedTask` calls.  
\- Removed several broad generic exception handlers.  
\- Removed repeated hardcoded values where named constants could be used.  
\- Removed unnecessary repeated session file reads.

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
