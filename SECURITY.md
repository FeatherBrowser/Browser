# Security reporting

Do not post credentials, browser profiles or exploit details in public issues.

Use the repository's **Security → Report a vulnerability** option when private vulnerability reporting is enabled. If that option is unavailable, request a private contact channel from the maintainer without disclosing vulnerability details publicly.

Include affected version, Windows/WebView2 versions, reproduction steps and impact using synthetic accounts. Never attach a real password CSV, cookies or your complete profile.

Feather has not undergone an independent security audit. Keep WebView2 Evergreen updated. The imported-password vault uses Windows DPAPI; this does not protect credentials from software already running as the same Windows user. “Private” windows are not an anonymity service.
