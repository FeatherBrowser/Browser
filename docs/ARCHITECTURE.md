# Architecture

This remains one WPF executable. Folders and namespaces separate responsibilities
without introducing extra assemblies or a dependency-injection framework.

## Code ownership

- **Domain/Models** contains data records only. Keep these free of WPF, WebView2
  and disk access. Serialized property names are part of the existing profile format.
- **Features** contains browser behaviour. `AddressResolver` is independent of
  WPF. `BlockerEngine` evaluates requests; `EdgeImporter` reads Edge profiles;
  `PasswordVault` manages imported credentials and delegates protection to DPAPI.
- **Infrastructure** owns persistence, resource loading and Windows integration.
  `WindowsDpapi` and `DefaultBrowserRegistrar` isolate platform APIs.
- **Presentation** owns controls, dialogs, WebView2 lifecycle and user input.
  `BrowserTab` belongs here because it holds WPF/WebView2 controls, not just data.

The application is not fully MVVM. MainWindow's partial files still share window
state. They are deliberately grouped by responsibility so event handlers and
WebView2 operations remain on their existing dispatcher and retain their existing
lifetime. A partial file is organizational, not an independent service.

## Shell map

| File suffix | Responsibility |
| --- | --- |
| `xaml.cs` | Shared window fields and constructor wiring |
| `Lifecycle` | Startup, private-profile cleanup, shutdown and session snapshots |
| `Tabs` / `TabLifecycle` | Tab creation, selection, closing, suspension and unloading |
| `WebView` | WebView2 creation, events, permissions/download wiring and recovery |
| `Workspaces` | Workspace sidebar and workspace actions |
| `Navigation` | Navigation controls and delegation to AddressResolver |
| `InternalPages` | Display and refresh built-in pages |
| `Settings` | Web-message dispatch, preferences and welcome presets |
| `SitePermissions` | Shield and site-permission controls |
| `Performance` | Resource telemetry, memory policies and performance dialog |
| `Menus` | Window, tab, history and favorites menus |
| `Downloads` | Download-list actions and opening files |
| `Importing` / `Passwords` | Import dialogs, default-browser action and autofill UI |
| `CommandPalette` | Command list, filtering and execution |
| `WindowChrome` | Window controls, fullscreen and keyboard shortcuts |

## Internal-page resources

Each internal page has an HTML template, CSS file and JavaScript file in
`Assets/Pages`. They are embedded with explicit logical names. `EmbeddedAssets`
loads and caches immutable asset text. `LoadPage` inserts CSS and JavaScript into
the HTML; page classes then replace data placeholders with the existing escaped
HTML or serialized JSON values. Templates stay bundled in the executable, so
navigation-to-string continues to work without loose-file paths or a local server.

Scripts injected into external pages live in `Assets/Scripts`. Never put a
credential or rendered page containing user data into the static resource cache.

## Extending the project

Put data-only records in Domain, reusable behaviour in Features and OS/storage
adapters in Infrastructure. Add only control-specific orchestration to the shell.
Use explicit imports and namespaces matching the folder structure. Avoid adding
global helper classes, empty interfaces or generic service containers without a
concrete consumer. For substantial future UI changes, extract a view or controller
with explicit inputs rather than continually expanding partial files.
