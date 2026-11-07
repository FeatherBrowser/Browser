# Feather's engine layer

Feather 2 no longer depends on Microsoft WebView2 or the Edge runtime. It renders pages with
open-source Chromium, embedded through the Chromium Embedded Framework (CEF) and CefSharp,
behind a thin layer Feather owns: `src/FeatherBrowser/Features/Engine`.

Writing a new rendering engine from scratch is a multi-year, multi-team effort (Ladybird is the
current example). Feather owns everything around the engine instead: tabs, lifecycle, blocking,
internal pages, permissions, downloads and UI. The engine sits behind one boundary, so it can be
swapped (for example for Servo) by reimplementing that folder.

## The boundary

Only files in `Features/Engine` may reference `CefSharp`. Everything else uses:

| Type | Role |
| --- | --- |
| `FeatherEngine` | Process-wide startup/shutdown, the profile folder, private (in-memory) request contexts, subprocess self-hosting. |
| `WebPage` | One tab's page. Navigation, scripting, zoom, find, print, DevTools, freeze/resume, cookies. Raises shell events on the WPF thread. |
| `InternalPages` | Serves Feather's own pages from `https://feather.internal/{kind}/{nonce}` and provides the `chrome.webview` bridge they use. |
| `INetworkPolicy` | Implemented by `BlockerEngine`. Decides blocking, Do Not Track and tracking-parameter rewriting on Chromium's IO thread. |
| `PermissionRequest`, `DownloadEventArgs`, … | Engine-neutral event payloads in `EngineTypes.cs`. |
| `EngineProcesses` | Lists Chromium's child processes and their roles for the Performance Center. |

## Threads

CEF calls handlers on its own UI and IO threads. `WebPage` never lets those reach the shell:
every event is queued with `Post`, which runs it on the page's dispatcher and drops it if the page
was disposed meanwhile. Code that runs on Chromium threads (`INetworkPolicy`, the shortcut
predicate) must be thread-safe and must not touch WPF. `BlockerEngine` keeps an immutable settings
snapshot, refreshed through `BrowserDataStore.SettingsSaved`, for exactly this reason.

## Processes

`Program.Main` calls `FeatherEngine.RunSubprocessIfRequested` first. Chromium starts its renderer,
GPU and utility processes by relaunching `FeatherBrowser.exe` with `--type=...`; those runs exit
there and never reach WPF or the single-instance check. This is required for self-contained
publishing and keeps a single executable in Task Manager.

## Internal pages and their bridge

Internal pages used to be `NavigateToString` documents at `about:blank`. Now:

1. The shell renders HTML as before and calls `WebPage.NavigateToInternalPage(kind, html)`.
2. `InternalPages.Publish` stores it under a random 128-bit nonce bound to that browser and returns
   `https://feather.internal/{kind}/{nonce}`. Only the newest nonce per page is kept.
3. A scheme handler registered for that host serves the page to the main frame only, with
   `no-store`, `X-Frame-Options: DENY` and `frame-ancestors 'none'`, and injects a
   `window.chrome.webview` shim so the page scripts did not need to change.
4. CefSharp's JavaScript bridge is enabled for the `https://feather.internal` origin only. Websites
   never see a `CefSharp` object.
5. `InternalPageMessagePolicy` still requires the per-render token, an internal page in the tab,
   and a main-frame sender on the internal origin.

Back/forward or reload onto an old internal URL asks the shell to render a fresh copy. Any other
navigation to `feather.internal` is refused.

## Tab lifecycle

| State | How |
| --- | --- |
| Hot | Visible page. |
| Background | Hidden; Chromium throttles timers and rendering on its own. Muted if *Mute background tabs* is on. |
| Sleeping | Frozen with the DevTools `Page.setWebLifecycleState("frozen")` call that Chrome uses for tab freezing: no script, no timers, state kept. |
| Cold | The browser is disposed and only the URL is kept. The page reloads on return. |
| Trim / Memory Guard | Also sends `Memory.simulatePressureNotification(critical)` so every Chromium process drops caches it can rebuild. |

Cold tabs cost only a small managed wrapper. The CefSharp control is not created until the tab is
first shown.

## Things Chromium's embedded mode leaves to Feather

Feather uses CEF's "Alloy" style, so it provides its own: permission prompts (the bar above the
page), find in page, the themed page context menu, popup handling (real sign-in popups open
natively, other `window.open` calls become tabs, unrequested popups are blocked), external
protocol confirmation, keyboard shortcuts (`IKeyboardHandler` forwards them), mouse back/forward
buttons, and audio detection. Chromium does not report tab audibility to embedders, so a small
script watches media elements and Web Audio contexts.

Not provided: Chromium's password manager and form autofill. Feather's imported-password vault
and Ctrl+Shift+P autofill still work.

## Updating Chromium

Bump `CefSharp.Wpf.NETCore` in `FeatherBrowser.csproj`, read the CefSharp release notes for
breaking changes, build, and run the Windows smoke checks in `docs/VALIDATION.md`. See
`SECURITY.md`: this is how Feather users receive Chromium security fixes.
