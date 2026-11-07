# Security reporting

Do not post credentials, browser profiles or exploit details in public issues.

Use the repository's **Security → Report a vulnerability** option when private vulnerability reporting is enabled. If that option is unavailable, request a private contact channel from the maintainer without disclosing vulnerability details publicly.

Include the affected Feather version, the Chromium version shown in **About Feather**, your Windows version, reproduction steps and impact using synthetic accounts. Never attach a real password CSV, cookies or your complete profile.

## Keeping the engine patched

Feather ships its own copy of Chromium (through CefSharp/CEF). Unlike the WebView2 runtime used by Feather 2.x, it is **not** updated by Windows. Chromium security fixes reach users only when Feather is rebuilt against a newer `CefSharp.Wpf.NETCore` package and released. Maintainers should:

- update the CefSharp package promptly after each CefSharp release (Dependabot is configured to open these pull requests), and
- cut a Feather release whenever the update contains Chromium security fixes.

Feather has not undergone an independent security audit. The imported-password vault uses Windows DPAPI; this does not protect credentials from software already running as the same Windows user. “Private” windows keep cookies and storage in memory only, but are not an anonymity service.
