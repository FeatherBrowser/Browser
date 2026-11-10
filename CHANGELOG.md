# Changelog

## 2.0.3

Feather 2.0.3 adds split view, reader view, password saving, picture-in-picture, tab search, per-site zoom and page memory per tab, and fixes a black page when switching back to a tab.

### Added

- **Split view:** right-click a tab and choose "Open in split view" to see it next to the current tab. Drag the divider to resize; click a side to make it the active tab. Switching to another tab shows it full size, and going back to either tab of the pair restores the split. Pages in a split are never frozen or unloaded while visible.
- **Reader view** (main menu or command palette): shows the page's article as clean, readable text without ads, menus or scripts. Choose it again to return to the full page.
- **Save passwords:** after you sign in or sign up on an HTTPS site, a bar offers to save or update the password in Feather's encrypted vault, or to never ask for that site. Never offered in Private windows; can be turned off in Settings → Privacy. Saved logins fill with Ctrl+Shift+P as before.
- **Picture-in-picture** (main menu or command palette): pops the page's video out into a floating window. Tabs with a floating video are never frozen or unloaded.
- **Search open tabs** (Ctrl+Shift+A): searches every open tab by title or address across all workspaces, most recently used first.
- **Zoom per site:** the zoom you choose for a site is remembered and applied whenever you open it, in every tab.
- **Page memory per tab:** each tab's tooltip and the Performance Center show how much memory the page's own scripts and content use, sampled with Feather's regular resource check.

### Changed

- Successful page loads no longer show a "Done" message in the status bubble; only load errors do.

### Fixed

- Switching back to a tab no longer shows a black page. A background tab was shown before it had finished unfreezing, so Chromium couldn't draw it.
- FeatherShield recognises negative redirect priorities such as `nooptext:-1`. (These two were listed under 2.0.2, but the 2.0.2 release still used the earlier FeatherShield commit.)
- An exception rule naming an unknown redirect resource no longer cancels every redirect for that request.
- Loading a site's icon no longer leaks request objects, and a slow icon download that finishes after its 8-second timeout can no longer write into memory that was already released.
- Code-quality fixes from CodeQL analysis (no change in behaviour): simpler dictionary lookups in settings sync and the find bar, shared Feather Shield caches updated only from static helpers, clearer permission-answer handling, and loops that filter their items explicitly.
- Process sampling for the Performance Center now closes its Windows process and snapshot handles automatically (through .NET safe handles), so an error partway through can no longer leak a handle.

## 2.0.2

Feather 2.0.2 is a reliability and security release. It fixes every open GitHub issue across sync, accounts, tab memory, passwords, privacy and startup, brings back status messages that disappeared with the bottom bar, and hardens device approval.

### Added

- A small status bubble in the bottom-left corner of the page shows the hovered link's address and short messages (import results, errors, permission changes, saves). They had gone nowhere since the bottom status bar was removed in 2.0.1.
- Database migration `004_device_approval_hardening.sql` for Feather Account servers. See `docs/ACCOUNT_SYNC_SETUP.md`.
- "Tabs from other devices" in the main menu lists the open tabs of your other synced devices; click one to open it here (#30).
- The History and Favorites library shows a "Show more" button instead of silently stopping at 700 rows.

### Changed

- Sync now keeps the content both devices last agreed on (DPAPI-protected, on this device only) and merges against it, so deletions and per-setting edits are understood. Existing installs switch to the new merge after their next successful sync.
- With open-tab sync on, each device now shares its own tabs separately. Another device's tabs are no longer written into this device's session file, where they used to overwrite or be overwritten by your own tabs (#30).
- All Feather windows now share one copy of your bookmarks, history and settings, so a second (or Private) window can no longer overwrite changes made in another window (#26).
- Joining an account (device approval or recovery) now merges this device's bookmarks and history into the account instead of replacing them (#31).
- The Release workflow runs the FeatherBrowser and FeatherShield unit tests and does not publish if they fail (#48).
- Uninstalling removes the per-user default-browser registration that Feather creates from Settings (#54).

### Fixed

- **Ad blocking**
  - Blocked XHR/fetch requests to ad and tracker hosts (such as doubleclick.net and googlesyndication.com) now fail like a real block instead of resolving with an empty stub, so ad-block test pages report them as blocked. Scripts, images and frames still receive harmless stubs so pages don't break.
- **Sync**
  - A bookmark or history entry deleted on one device no longer comes back when another device has also made changes (#12).
  - Changing different settings on two devices keeps both changes instead of one device overwriting the other (#56).
  - Edits made while a sync request is running are no longer overwritten by the older captured state (#11).
  - Syncing with no changes no longer uploads a new revision every time (#17).
  - Bookmarks and history entries whose addresses differ only in path or query case (`/File` vs `/file`) are kept separate, in local data and in sync (#19).
  - If synced data cannot be saved to disk, sync stops and reports it instead of recording the change as done. Settings and favorites changes also say when they could not be written (#25).
  - First-time encrypted sync setup can no longer leave a vault whose recovery key you never saw. The setup is kept (DPAPI-protected) until your recovery key has been shown and this device is registered; if anything fails in between, trying again finishes the same setup and shows the key (#35).
  - Closing a window during a sync no longer disposes the sync engine and network clients while requests are still using them (#36).
- **Account and security**
  - Device approval encrypts only to the key whose pairing code you confirmed; a key that changes between display and approval is rejected (#39).
  - The server can no longer mark a device approved without a real key transfer from an approved device, and a recovered device can't replace an approved device's key (#38, requires migration 004).
  - Signing out can no longer be undone by a token refresh that was still in flight (#32).
  - Cancelling the 2FA code prompt no longer shows "2FA verified" (#33).
  - Setting up or verifying 2FA refreshes an expired access token first instead of failing (#34).
  - Starting Feather offline no longer leaves automatic sync stopped once you reconnect, and a successful manual sync restarts it (#37).
  - Recovery backup files are size- and work-limited, so a damaged or hostile file cannot freeze Feather with unbounded key derivation (#40).
- **Passwords**
  - If the saved-password vault can't be read, Feather now says so and refuses to overwrite it on the next import. Clearing it keeps a backup of the unreadable file (#41).
  - Autofill fills the username and password of the account you picked together, instead of pairing your password with a different prefilled username (#42).
- **Privacy and site data**
  - Permission choices made from the site menu in a Private window stay in that window and are never saved to the normal profile (#27). Reset permissions also clears them.
  - "Clear cookies for this site" removes the site's cookies on every path, not only those matching the current page (#51).
- **Tabs and memory**
  - Automatic unloading no longer discards pages with unsaved typing. Text you typed into a form or editor and haven't submitted or cleared keeps the tab loaded (it can still be suspended) (#15).
  - Low Memory mode's loaded-tab budget respects the background grace period, so quickly switching tabs doesn't reload them (#16).
  - Adaptive Memory Mode now works: the loaded-tab budget halves at 75% of the RAM Guard limit and drops to one tab at the limit (#24).
  - Switching workspaces with "Cold unload other workspaces" also unloads the tab that was active in the old workspace (#46).
  - Turning off "Keep this site alive" also removes a matching parent-domain rule (the menu now shows "via example.com"), and background unloading resumes afterwards (#47).
  - Pressing Reload on a tab whose wake-up failed shows the recovered page instead of leaving it hidden (#45).
- **Startup, settings and downloads**
  - Library search covers all stored bookmarks and history, not just the first 1,000 bookmarks and 1,500 history entries (#21).
  - Opening Feather from a link restores your previous session and adds the link, instead of replacing the saved session (#29).
  - Links opened while Feather is still starting are queued until it's ready instead of being dropped after five seconds (#55).
  - Double-clicking a local `.html` file opens it when Feather is the HTML handler (#53).
  - In Settings, Discard returns to the last applied values rather than the values from when the page opened (#23).
  - Downloads cut off by a crash or forced exit are marked Interrupted on the next start instead of staying In progress forever (#52).

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
