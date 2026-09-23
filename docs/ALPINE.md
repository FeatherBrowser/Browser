# Alpine implementation

`Assets/Pages/glass.css` supplies the shared internal-page surface theme. `EmbeddedAssets.LoadPage` appends it after each page stylesheet. `landscape.svg` is an embedded offline asset inserted into the start page.

`MainWindow.Home.cs` owns dashboard telemetry and validated quick-link persistence. `QuickLink` is a small domain model stored inside `BrowserSettings`. The dashboard sends messages through the existing WebView2 bridge; `MainWindow.Settings.cs` routes commands and limits privileged messages to internal `about:blank` pages. HTTP/HTTPS quick-link validation also runs in the host.

The home page never polls process APIs itself. The existing resource timer publishes telemetry to loaded home tabs. New tabs request an initial snapshot through `home-ready`. Inactive home tabs continue to follow the existing suspension/unload policy.

Native styles live in `Presentation/Themes/DarkTheme.xaml`; title, tabs, toolbar and sidebar layout live in `MainWindow.xaml`. Windows system dialogs are unchanged.
