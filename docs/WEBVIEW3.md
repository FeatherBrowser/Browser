# Feather WebView3 — tab resource manager

"WebView3" is the name of Feather's tab resource manager. Since Feather 3 it runs on Feather's own engine layer over Chromium (see [ENGINE.md](ENGINE.md)), not WebView2. Website rendering, JavaScript, GPU compositing and security fixes come from the Chromium build bundled with Feather.

## Behaviour

- One in-flight initialization per tab avoids creating duplicate pages during rapid selection. Cold tabs do not create a Chromium control at all until first shown.
- A tab that finishes loading after another tab was selected stays hidden.
- Sleeping tabs are frozen with Chromium's page-lifecycle freeze (the mechanism Chrome uses), so they run no script or timers but keep their state. Suspension is rechecked after the freeze completes; a tab selected meanwhile is resumed.
- A grace period keeps recently hidden tabs loaded briefly to avoid reload churn. Emergency memory trimming and gaming may bypass it.
- Pinned tabs, keep-alive sites, active downloads, and audio-playing tabs (when protection is enabled) are excluded from automatic suspension/unloading. This may put actual usage above the configured budget.
- Minimized active pages are hidden; eligible tabs are unloaded or suspended according to existing preferences. Restoring resumes/recreates the selected tab.
- Resource sampling runs off the UI thread with one sample in flight: roughly every five seconds normally and fifteen seconds while minimized. Repeated UI updates reuse the snapshot.
- Only the visible home page receives telemetry updates. Unchanged session snapshots are not rewritten; unsuccessful saves remain eligible for retry.
- Hardware acceleration and Chromium's process isolation remain enabled. Reduced hidden-page animation and script work may reduce GPU/CPU use, but this layer cannot cap active-page GPU utilization or implement a new compositor.

## Defaults

Feather 2.x forced a one-tab, five-second unload policy on every install. Feather 3 freezes background tabs inside Chromium instead, so new installs and installs still on that forced policy start on **Balanced**. Settings you changed yourself are kept.

## Profiles

Select **Settings → Performance → Apply a performance profile**. Applying a profile changes the listed resource settings immediately and reloads Settings. Existing preferences are preserved until you choose a profile.

| Profile | Loaded budget | Sleep | Unload | Switching grace | Guard |
|---|---:|---:|---:|---:|---:|
| Balanced | 3 | 10s | 60s | 10s | 1200 MB |
| Memory Saver | 1 | 3s | 20s | 3s | 700 MB |
| Gaming | 1 | 1s | 8s | 0s | 600 MB |
| Responsive | 6 | 30s | 180s | 30s | 2000 MB |

These are eligibility delays, not guarantees that all tabs stay loaded until the unload time. Tab-budget enforcement can unload earlier, after the grace period. Balanced and Responsive keep other workspaces available; Saver and Gaming enable workspace unloading. Adaptive pressure can reduce the loaded-tab budget.

Cold unloading releases the page's browser and restores its URL later. Unsaved page state can be lost. Pin tabs with forms, calls or other important live state, or add their domains to Keep Alive sites. Capture/call detection and form-state serialization are not implemented.

## Memory and CPU measurements

The primary metric is **Private RAM**, using `PROCESS_MEMORY_COUNTERS_EX2.PrivateWorkingSetSize` where available. Older Windows/API failures fall back to **Private commit** for the entire sample, explicitly labelled. Commit includes private virtual-memory commitments and is not resident physical RAM. Partial process samples are marked and do not trigger emergency trimming.

Performance Center shows per-process private RAM, private commit and CPU. It separately labels **combined working sets**, which can repeatedly count shared pages. CPU is calculated from process CPU-time deltas and normalized over logical processors. The first sample shows a warm-up state. GPU utilization/VRAM are not measured.

Compare the same process group, timestamp and metric in Task Manager. Numbers need not match the single Feather executable or a differently configured Task Manager column. All Feather windows share one set of Chromium processes, including private windows, so the Performance Center shows the whole browser.

Microsoft references:
- https://learn.microsoft.com/windows/win32/api/psapi/ns-psapi-process_memory_counters_ex2
- https://learn.microsoft.com/windows/win32/api/psapi/nf-psapi-getprocessmemoryinfo
- https://chromedevtools.github.io/devtools-protocol/tot/Page/#method-setWebLifecycleState
- https://chromedevtools.github.io/devtools-protocol/tot/Memory/#method-simulatePressureNotification

## Benchmark before claiming savings

Compare Feather 2.x (WebView2) and this build sequentially on the same Windows PC, same pages and settings. Record private RAM, private commit, group CPU, GPU engine utilization in Task Manager, and tab-switch latency after warm-up. Test: one active tab, ten inactive tabs, audio playback, rapid tab switching, and five minutes minimized. Repeat each scenario. No percentage improvements have been measured or claimed for this release.
