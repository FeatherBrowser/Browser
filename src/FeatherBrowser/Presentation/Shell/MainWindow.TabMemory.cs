using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using FeatherBrowser.Features.Engine;
using FeatherBrowser.Presentation.Tabs;

namespace FeatherBrowser.Presentation.Shell;

public partial class MainWindow
{
    private bool IsProtectedTab(BrowserTab tab)
    {
        if (tab.IsClosed || tab.IsPinned || tab.HasActiveDownload ||
            tab.LoadingTask is { IsCompleted: false } ||
            (tab == _activeTab && WindowState != WindowState.Minimized) ||
            (_store.Settings.KeepAudioTabsLoaded && tab.IsPlayingAudio))
            return true;

        if (!Uri.TryCreate(tab.LastAddress, UriKind.Absolute, out Uri? page))
            return false;

        return _store.Settings.KeepAliveSites.Any(rule =>
        {
            string host = rule.Trim().TrimStart('*', '.');
            if (Uri.TryCreate(host, UriKind.Absolute, out Uri? site) &&
                !string.IsNullOrEmpty(site.Host))
                host = site.Host;
            host = host.TrimEnd('/');
            return host.Length > 0 &&
                (page.Host.Equals(host, StringComparison.OrdinalIgnoreCase) ||
                 page.Host.EndsWith('.' + host, StringComparison.OrdinalIgnoreCase));
        });
    }

    private bool ShouldManageBackgroundTabs() =>
        _ecoMode || _store.Settings.LowMemoryMode || _store.Settings.GameMode ||
        (WindowState == WindowState.Minimized && _store.Settings.HibernateWhenMinimized);

    private void ScheduleBackgroundLifecycle(BrowserTab tab)
    {
        CancelSleepSchedule(tab);
        if (_isClosing || !tab.IsLoaded || IsProtectedTab(tab) || !ShouldManageBackgroundTabs())
            return;

        _ = RunBackgroundLifecycleAsync(tab);
    }

    private async Task RunBackgroundLifecycleAsync(BrowserTab tab)
    {
        using var cancellation = new CancellationTokenSource();
        tab.SleepCancellation = cancellation;
        CancellationToken token = cancellation.Token;
        var view = tab.View;
        bool IsCurrent() => !token.IsCancellationRequested && !_isClosing &&
            !tab.IsClosed && tab.IsLoaded && ReferenceEquals(tab.View, view) &&
            ReferenceEquals(tab.SleepCancellation, cancellation);

        try
        {
            int grace = Math.Clamp(_store.Settings.BackgroundGraceSeconds, 0, 60);
            int sleepAfter = Math.Max(grace, Math.Clamp(_store.Settings.SleepAfterSeconds, 1, 60));
            int unloadAfter = Math.Max(sleepAfter, Math.Clamp(_store.Settings.UnloadAfterSeconds, 2, 300));
            await Task.Delay(TimeSpan.FromSeconds(sleepAfter), token);
            if (!IsCurrent() || IsProtectedTab(tab) || !ShouldManageBackgroundTabs())
                return;

            HideTab(tab);
            await view.TrySuspendAsync();

            if (!IsCurrent())
            {
                if (!_isClosing && !tab.IsClosed && tab.IsLoaded &&
                    ReferenceEquals(tab.View, view) && tab == _activeTab &&
                    WindowState != WindowState.Minimized)
                    view.Resume();
                return;
            }

            if (IsProtectedTab(tab) || !ShouldManageBackgroundTabs())
            {
                view.Resume();
                return;
            }

            tab.UpdateStateIndicator();
            await Task.Delay(TimeSpan.FromSeconds(unloadAfter - sleepAfter), token);
            if (IsCurrent() && !IsProtectedTab(tab) &&
                (_store.Settings.LowMemoryMode || _store.Settings.GameMode ||
                 (WindowState == WindowState.Minimized && _store.Settings.HibernateWhenMinimized)) &&
                ColdUnloadTab(tab))
            {
                UpdateWorkspaceSidebar();
                UpdateResourceText();
                SaveSessionSnapshotIfEnabled();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (InvalidOperationException ex)
        {
            Trace.TraceWarning($"Background tab lifecycle stopped: {ex.Message}");
        }
        finally
        {
            if (ReferenceEquals(tab.SleepCancellation, cancellation))
                tab.SleepCancellation = null;
        }
    }

    private bool HibernateTab(BrowserTab tab) => ColdUnloadTab(tab);

    private bool ColdUnloadTab(BrowserTab tab)
    {
        if (_isClosing || !tab.IsLoaded || IsProtectedTab(tab))
            return false;

        var oldView = tab.View;
        if (!tab.IsInternalPage)
        {
            string source = oldView.Source;
            if (!string.IsNullOrWhiteSpace(source) &&
                !source.Equals("about:blank", StringComparison.OrdinalIgnoreCase) &&
                !source.StartsWith("data:", StringComparison.OrdinalIgnoreCase) &&
                !InternalPages.IsInternalUrl(source))
                tab.LastAddress = source;
        }

        var replacement = CreatePageFor(tab);
        CancelSleepSchedule(tab);
        tab.IsLoaded = false;
        tab.IsPlayingAudio = false;
        tab.NeedsContentRestore = true;
        tab.View = replacement;
        RemovePageControl(oldView);
        SetTabLoading(tab, false);
        ForgetPageDownloads(tab, oldView);
        DismissPermissionPrompt(tab, promptId: null);

        if (tab.Header.Tag is TabHeaderVisuals visuals)
            visuals.FaviconRevision++;
        if (ReferenceEquals(_videoFullscreenTab, tab))
        {
            _videoFullscreenTab = null;
            ApplyFullscreen();
        }

        oldView.Dispose();

        tab.UpdateStateIndicator();
        return true;
    }

    private void EnforceLoadedTabBudget()
    {
        if (_isClosing || (!_store.Settings.LowMemoryMode && !_store.Settings.GameMode))
            return;

        int limit = _store.Settings.GameMode ? 1 : Math.Clamp(_store.Settings.MaxLoadedTabs, 1, 8);
        TimeSpan grace = TimeSpan.FromSeconds(_store.Settings.GameMode ? 0 : Math.Clamp(_store.Settings.UnloadAfterSeconds, 2, 300));
        DateTime now = DateTime.UtcNow;
        int loaded = _tabs.Count(tab => !tab.IsClosed && tab.IsLoaded);
        int unloaded = 0;
        foreach (BrowserTab tab in _tabs.Where(tab => !tab.IsClosed && tab.IsLoaded && tab != _activeTab &&
                                                      now - tab.HiddenSinceUtc >= grace)
                     .OrderBy(tab => tab.LastActivatedUtc).ToList())
        {
            if (loaded <= limit)
                break;
            if (ColdUnloadTab(tab))
            {
                loaded--;
                unloaded++;
            }
        }
        if (unloaded > 0)
        {
            UpdateWorkspaceSidebar();
            SaveSessionSnapshotIfEnabled();
        }
    }

    private void TrimMemoryNow()
    {
        if (_isClosing)
            return;

        int unloaded = _tabs
            .Where(tab => !tab.IsClosed && tab.IsLoaded && tab != _activeTab)
            .OrderBy(tab => tab.LastActivatedUtc)
            .ToList()
            .Count(ColdUnloadTab);

        if (_activeTab is { IsLoaded: true } active)
            _ = active.View.TrimMemoryAsync();

        UpdateWorkspaceSidebar();
        UpdateResourceText();
        SaveSessionSnapshotIfEnabled();
        StatusText.Text = unloaded > 0
            ? $"Unloaded {unloaded} background tab(s) to free memory"
            : "No eligible background tabs to unload";
    }
}

