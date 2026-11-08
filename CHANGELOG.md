# Changelog

## 2.0.1

Feather 2.0.1 strengthens FeatherShield with redirect and scriptlet filter support and anti-adblock protection, removes the bottom status bar, and fixes sync, importing and settings navigation.

### Added

- FeatherShield now supports `$redirect` / `$redirect-rule` filters. Instead of being cancelled outright, blocked requests can be answered with a harmless local stub (empty script, transparent image, empty JSON, …), so pages that depend on a blocked script keep working.
- FeatherShield now supports scriptlet filters (`##+js(...)`). Scriptlets are injected into matching frames to neutralise anti-adblock walls and similar page scripts, are skipped on allowlisted sites, and are cached per host.
- FeatherFilters adds anti-adblock detection rules and a scheduled workflow that keeps the bundled filter lists up to date automatically.
- FeatherFilters adds more tracking and telemetry endpoints to `network-domains.txt`, and re-enables the Outbrain iframe rule.
- Third-party notices now list the bundled EasyList, EasyPrivacy, uBlock filters and uBlock Quick fixes snapshots and their licenses.
- New tests for FeatherShield's anti-adblock, redirect and scriptlet handling.

### Changed

- Removed the bottom resource/status bar and its "Show resource/status bar" setting. Memory and process details remain available in the Performance center.
- A New Tab page with no back history is now unloaded when you switch away from it, freeing its memory.
- Releases are now published automatically when a commit pushed to `main` includes `vMAJOR.MINOR.PATCH` in its message. The release tag is created on the commit being released.
- CI uses `actions/setup-dotnet@v6` and `actions/upload-artifact@v7`.

### Fixed

- Settings links that open a specific section now land on that section instead of the first page.
- Sync could fail to read the remote encrypted sync record because of mismatched JSON property casing.
- FeatherImport now interprets Chromium timestamps as UTC, so imported history and bookmark dates are no longer shifted by your time zone.

## 2.0.0

Feather 2.0 is a major update focused on making the browser cleaner, faster, more reliable, easier to maintain and more polished. It replaces Microsoft WebView2 with Feather's own engine layer over open-source Chromium (CEF through CefSharp 154 / Chromium 154). No Edge or WebView2 runtime is needed, and the installer works offline without administrator rights.

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
\- Added standalone **FeatherShield** library for Feather’s ad, tracker and cosmetic filtering engine.
\- Added standalone **FeatherFilters** repository for Feather’s maintained ad, tracker, telemetry and tracking-parameter filter data.
\- Added standalone **FeatherImport** library for Edge and Chromium browser data importing.
\- Added external module integration so Feather Browser can use FeatherShield, FeatherFilters and FeatherImport while keeping their implementations separate from the main browser codebase.
\- Added dedicated tests and build configuration for FeatherShield and FeatherImport.
\- Added versioned external dependency tracking for Feather’s standalone repositories.
\- Added optional Feather Account sign-up, sign-in and DPAPI-protected session restore.
\- Added authenticator-app TOTP two-factor authentication with AAL2 enforcement for sensitive sync operations.
\- Added zero-knowledge encrypted sync for bookmarks, settings, workspaces and quick links, with optional history and open-tab syncing.
\- Added approved-device pairing so an existing Feather device can authorize a new installation without exposing the Sync Master Key to the server.
\- Added recovery-key restore and password-protected `.feather-recovery` backup files for removable/offline storage.
\- Added configurable automatic encrypted sync with private-window exclusion.
\- Added Account & Sync settings UI, device approval controls, pairing fingerprints and sync status.
\- Added Supabase migrations for encrypted sync storage, MFA-gated access, device registration, recovery registration and pending-device denial.
\- Added account security and deployment documentation.
- Site permission bar for camera, microphone, location, notifications, screen sharing, clipboard and more, remembered per site (in memory only for private windows).
- Site information button in the address bar: padlock for HTTPS, warning for HTTP. It opens the site menu, which was previously unreachable.
- Find in page (Ctrl+F, F3, Shift+F3), Print (Ctrl+P), Developer tools (F12, Ctrl+Shift+I), Chrome-style zoom steps, and a zoom submenu.
- Ctrl+1…9, Ctrl+PgUp/PgDn, Ctrl+F4, Alt+D, F6, Alt+Home, hard reload (Ctrl+Shift+R, Shift+F5), and a shortcuts reference in Settings → Appearance.
- Page context menus in Feather's theme, with "Open link in new tab", "Copy link address", "Open image in new tab", "Search the web for…" and "Inspect".
- Mouse back/forward buttons navigate inside pages.
- Tab loading spinner, visible audio/mute/sleeping/unloaded/pinned markers, and click-to-mute on a tab's speaker icon.
- Tabs opened from a page appear next to it.
- Sign-in and payment popups open as real popup windows, so `window.opener` flows work. Unrequested popups are blocked.
- Confirmation before a page opens an external app (`mailto:`, `zoommtg:`, …).
- Download progress in the status bar, a warning before closing a tab with a running download, and safe, non-overwriting download file names.
- Windows 11 rounded corners, a dark window border and dark system menus.

### Changed

- Added `Features/Engine`, the only code that talks to Chromium. The shell uses `WebPage` and `FeatherEngine`, so the engine can be replaced in one place. See `docs/ENGINE.md`.
- FeatherBrowser.exe now also runs Chromium's renderer, GPU and utility processes, so a self-contained publish needs no separate subprocess executable.
- Internal pages are served from the reserved `https://feather.internal` origin, with per-page nonces, no-store, frame blocking and a bridge that only that origin can reach. Their scripts are unchanged.
- Private windows use an in-memory Chromium profile instead of a temporary folder on disk.
- Background tabs are now frozen with Chromium's page-lifecycle freeze (the mechanism Chrome uses) instead of being torn down. Tabs keep their state and use no CPU while sleeping.
- Installs still on the forced one-tab, five-second unload policy move to the Balanced profile, so switching tabs no longer reloads pages. Custom settings are kept.
- Feather Shield checks requests on Chromium's network thread instead of the UI thread.
- Filter lists are parsed once, in the background, and shared by every window. They were previously parsed twice at startup, and again on the UI thread every time any setting changed.
- The window appears before Chromium starts. Unloaded tabs no longer create a browser control at all.
- Trim memory and Memory Guard also ask every Chromium process to drop rebuildable caches.
- The Performance Center now reports real private RAM per process. It had been falling back to private commit on every system.
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
\- Moved ad and tracker matching logic out of the main Browser project and into **FeatherShield**.
\- Moved cosmetic filtering scripts out of the Browser project and into **FeatherShield**.
\- Moved built-in blocking domains, tracking tokens, tracking parameters and exception data into **FeatherFilters**.
\- Changed FeatherShield to use browser-independent request and resource types instead of depending directly on WebView2.
\- Changed Feather Browser to use a lightweight WebView2 adapter when communicating with FeatherShield.
\- Moved Edge and Chromium importing logic out of the Browser project and into **FeatherImport**.
\- Changed FeatherImport to return browser-independent bookmark, history and session models instead of writing directly to Feather Browser’s data store.
\- Changed Feather Browser to merge imported data into its own persistence layer after FeatherImport completes.
\- Moved SQLite ownership for Chromium history importing into FeatherImport instead of the main Browser project.
\- Changed filter lists to be shipped as external FeatherFilters data instead of being hardcoded inside the browser executable.
\- Updated the Windows build and release workflow to restore Feather’s external repositories automatically.
\- Updated publishing so FeatherShield, FeatherImport and FeatherFilters are included with release builds.
\- Improved separation between browser UI, WebView2 integration, blocking, filter data and external browser importing.
- The About box shows the Chromium and CEF versions.
- Chromium security fixes now arrive through Feather releases (Dependabot tracks CefSharp). See SECURITY.md
- Website sign-ins and cookies from 2.x (stored in WebView2's profile) do not carry over. Favorites, history, settings, sessions and imported passwords do.

### Fixed

- The Ctrl+K command palette was hidden behind the page.
- Alt+Left / Alt+Right never worked, because WPF reports Alt combinations as a system key.
- A tab's audio, pinned and unloaded markers were never shown.
- Tracking-parameter stripping could cancel form submissions. It now rewrites only GET navigations.
- A tab that crashed or unloaded during a download stayed protected from sleeping forever.
- Reopen closed tab (Ctrl+Shift+T) now restores Settings and Library pages too.
- Imported Edge history timestamps used local time, which put Chromium's epoch in the year 1600 in some time zones.
- Ad iframes are classified as subdocuments, so strict blocking now applies to them.
- The internal-page UI checks targeted widgets that no longer existed, and their render script referenced a missing asset.

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
