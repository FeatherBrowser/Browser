# Feather WebView3 — resource management layer

This is Feather's own resource-management layer over Microsoft WebView2. It is not a new rendering engine, a Microsoft product, or a replacement for Chromium. Website rendering, JavaScript, GPU compositing and security updates still come from WebView2.

## Behaviour

- One in-flight initialization per tab avoids creating duplicate WebViews during rapid selection.
- A tab that finishes loading after another tab was selected stays hidden.
- Background suspension is rechecked after awaiting WebView2. A tab selected during suspension is resumed.
- A grace period keeps recently hidden tabs loaded briefly to avoid reload churn. Emergency memory trimming and gaming may bypass it.
- Pinned tabs, keep-alive sites, active downloads, and audio-playing tabs (when protection is enabled) are excluded from automatic suspension/unloading. This may put actual usage above the configured budget.
- Minimized active views are hidden; eligible tabs are unloaded or suspended according to existing preferences. Restoring resumes/recreates the selected tab.
- Resource sampling runs off the UI thread with one sample in flight: roughly every five seconds normally and fifteen seconds while minimized. Repeated UI updates reuse the snapshot.
- Only the visible home page receives telemetry updates. Unchanged session snapshots are not rewritten; unsuccessful saves remain eligible for retry.
- Hardware acceleration and Chromium's process isolation remain enabled. Reduced hidden-page animation and script work may reduce GPU/CPU use, but this layer cannot cap active-page GPU utilization or implement a new compositor.

## Profiles

Select **Settings → Performance → Apply a performance profile**. Applying a profile changes the listed resource settings immediately and reloads Settings. Existing preferences are preserved until you choose a profile.

| Profile | Loaded budget | Sleep | Unload | Switching grace | Guard |
|---|---:|---:|---:|---:|---:|
| Balanced | 3 | 10s | 60s | 10s | 1200 MB |
| Memory Saver | 1 | 3s | 20s | 3s | 700 MB |
| Gaming | 1 | 1s | 8s | 0s | 600 MB |
| Responsive | 6 | 30s | 180s | 30s | 2000 MB |

These are eligibility delays, not guarantees that all tabs stay loaded until the unload time. Tab-budget enforcement can unload earlier, after the grace period. Balanced and Responsive keep other workspaces available; Saver and Gaming enable workspace unloading. Adaptive pressure can reduce the loaded-tab budget.

Cold unloading releases the WebView and restores its URL later. Unsaved page state can be lost. Pin tabs with forms, calls or other important live state, or add their domains to Keep Alive sites. Capture/call detection and form-state serialization are not implemented.

## Memory and CPU measurements

The primary metric is **Private RAM**, using `PROCESS_MEMORY_COUNTERS_EX2.PrivateWorkingSetSize` where available. Older Windows/API failures fall back to **Private commit** for the entire sample, explicitly labelled. Commit includes private virtual-memory commitments and is not resident physical RAM. Partial process samples are marked and do not trigger emergency trimming.

Performance Center shows per-process private RAM, private commit and CPU. It separately labels **combined working sets**, which can repeatedly count shared pages. CPU is calculated from process CPU-time deltas and normalized over logical processors. The first sample shows a warm-up state. GPU utilization/VRAM are not measured.

Compare the same process group, timestamp and metric in Task Manager. Numbers need not match the single Feather executable or a differently configured Task Manager column. Multiple Feather windows sharing a WebView2 environment can share child processes.

Microsoft references:
- https://learn.microsoft.com/windows/win32/api/psapi/ns-psapi-process_memory_counters_ex2
- https://learn.microsoft.com/windows/win32/api/psapi/nf-psapi-getprocessmemoryinfo
- https://learn.microsoft.com/dotnet/api/microsoft.web.webview2.core.corewebview2.memoryusagetargetlevel

## Benchmark before claiming savings

Compare the Alpine baseline and this build sequentially on the same Windows PC, same WebView2 runtime, same pages and settings. Record private RAM, private commit, group CPU, GPU engine utilization in Task Manager, and tab-switch latency after warm-up. Test: one active tab, ten inactive tabs, audio playback, rapid tab switching, and five minutes minimized. Repeat each scenario. No percentage improvements have been measured or claimed for this release.
