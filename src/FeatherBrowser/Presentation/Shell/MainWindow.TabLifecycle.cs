using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using FeatherBrowser.Presentation.Tabs;

namespace FeatherBrowser.Presentation.Shell;

public partial class MainWindow
{
    private async Task SelectTabAsync(BrowserTab tab)
    {
        if (tab.IsClosed || (_activeTab == tab && tab.IsLoaded))
            return;

        ActivateWorkspaceFor(tab);

        BrowserTab? previous = _activeTab;
        _activeTab = tab;
        if (previous is not null && !ReferenceEquals(previous, tab))
            LeaveVideoFullscreen(previous);
        ApplyFullscreen();

        UpdateTabSelection(tab);
        CancelSleepSchedule(tab);
        tab.LastActivatedUtc = DateTime.UtcNow;

        if (!await TryEnsureTabLoadedAsync(tab))
            return;

        if (tab.IsClosed || _isClosing)
            return;

        if (_activeTab != tab)
        {
            HideTab(tab);
            ScheduleBackgroundLifecycle(tab);
            return;
        }

        ShowActiveTab(tab);

        if (previous is not null &&
            previous != tab &&
            !previous.IsClosed &&
            previous.IsLoaded)
        {
            MoveTabToBackground(previous);
        }

        RefreshActiveTabUi(tab);
        EnforceLoadedTabBudget();
        UpdateWorkspaceSidebar();
        UpdateResourceText();
        SaveSessionSnapshotIfEnabled();
    }

    private void ActivateWorkspaceFor(BrowserTab tab)
    {
        if (string.Equals(
                tab.Workspace,
                _store.Settings.ActiveWorkspace,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _store.Settings.ActiveWorkspace = tab.Workspace;
        _store.SaveSettings();
        RefreshWorkspaceTabVisibility();
        UpdateWorkspaceSidebar();
    }

    private static void CancelSleepSchedule(BrowserTab tab)
    {
        tab.SleepCancellation?.Cancel();
        tab.SleepCancellation?.Dispose();
        tab.SleepCancellation = null;
    }

    private async Task<bool> TryEnsureTabLoadedAsync(BrowserTab tab)
    {
        try
        {
            await EnsureTabLoadedAsync(tab);
            HookTabFavicon(tab);
            await UpdateTabFaviconAsync(tab);
            return true;
        }
        catch (OperationCanceledException ex)
        {
            if (!tab.IsClosed && !_isClosing && _activeTab == tab)
                ReportTabWakeFailure("Tab wake was cancelled", ex);

            return false;
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException or COMException)
        {
            if (!tab.IsClosed && !_isClosing && _activeTab == tab)
                ReportTabWakeFailure("Tab wake failed", ex);
            return false;
        }
    }

    private void ReportTabWakeFailure(string reason, Exception exception)
    {
        Trace.TraceWarning($"{reason}: {exception.Message}");
        StatusText.Text = "Could not wake this tab";

        MessageBox.Show(
            $"Feather could not recreate this tab.\n\n{exception.Message}",
            "Feather tab",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void ShowActiveTab(BrowserTab tab)
    {
        if (tab.View.CoreWebView2.IsSuspended)
            tab.View.CoreWebView2.Resume();

        tab.View.CoreWebView2.IsMuted = false;
        tab.View.Visibility = Visibility.Visible;
        Panel.SetZIndex(tab.View, 10);
    }

    private void MoveTabToBackground(BrowserTab tab)
    {
        tab.HiddenSinceUtc = DateTime.UtcNow;
        HideTab(tab);

        tab.View.CoreWebView2.IsMuted =
            _store.Settings.MuteBackgroundTabs ||
            _store.Settings.GameMode;

        ScheduleBackgroundLifecycle(tab);
    }

    private static void HideTab(BrowserTab tab)
    {
        tab.View.Visibility = Visibility.Collapsed;
        Panel.SetZIndex(tab.View, 0);
    }

    private void RefreshActiveTabUi(BrowserTab tab)
    {
        tab.Header.BringIntoView();
        UpdateTabStripLayout();

        AddressBox.Text = DisplayAddress(tab);
        Title = $"{tab.Title.Text} — Feather";
        TitleText.Text = tab.Title.Text;

        UpdateNavigationButtons();
        UpdateBookmarkButton();
        UpdateFeatureButtons();
    }

    private async Task CloseTabAsync(BrowserTab tab)
    {
        if (tab.IsClosed)
            return;

        bool wasActive = _activeTab == tab;
        int workspaceIndex = GetWorkspaceTabIndex(tab);

        if (!string.IsNullOrWhiteSpace(tab.LastAddress))
            _closedTabs.Push(tab.LastAddress);

        tab.IsClosed = true;
        if (ReferenceEquals(_videoFullscreenTab, tab))
        {
            _videoFullscreenTab = null;
            ApplyFullscreen();
        }
        CancelSleepSchedule(tab);

        TabStrip.Children.Remove(tab.Header);
        UpdateTabStripLayout();

        if (tab.IsLoaded)
            BrowserHost.Children.Remove(tab.View);

        _tabs.Remove(tab);
        tab.View.Dispose();

        UpdateWorkspaceSidebar();

        if (!wasActive)
        {
            UpdateResourceText();
            SaveSessionSnapshotIfEnabled();
            return;
        }

        _activeTab = null;

        List<BrowserTab> workspaceTabs = _tabs
            .Where(IsTabInActiveWorkspace)
            .ToList();

        if (workspaceTabs.Count == 0)
        {
            await AddTabAsync(workspace: _store.Settings.ActiveWorkspace);
            return;
        }

        int nextIndex = Math.Clamp(workspaceIndex, 0, workspaceTabs.Count - 1);
        await SelectTabAsync(workspaceTabs[nextIndex]);
    }

    private int GetWorkspaceTabIndex(BrowserTab tab)
    {
        int index = 0;

        foreach (BrowserTab candidate in _tabs)
        {
            if (!IsTabInActiveWorkspace(candidate))
                continue;

            if (candidate == tab)
                return index;

            index++;
        }

        return 0;
    }
}
