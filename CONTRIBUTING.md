# Contributing

Feather is a Windows WPF application targeting .NET 9. Install the SDK and WebView2 Runtime, then follow the README to build and run it.

Keep changes focused. Explain the problem, the behaviour changed and how you tested it. Follow the existing folder structure and formatting. Do not add browser flags that disable sandboxing, certificate validation or site isolation.

Run `scripts/build.ps1` and `scripts/check.ps1`. Changes to native menus, WebView2 lifecycle or Windows integration also need a Windows smoke test. Changes to internal pages can use the optional `tests/ui` checks. Add regression checks for meaningful behaviour, rather than tests that duplicate the implementation.

Never commit browser profiles, cookies, browsing history, password exports, credentials or build output. Use synthetic test data. Redact screenshots and logs before posting an issue.

Changes are contributed under the repository's MIT license. Include attribution and licensing details for new third-party code or assets.
