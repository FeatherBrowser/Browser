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
        if (!tab.IsStartPage && !AddressBox.IsKeyboardFocusWithin && !FindBar.IsKeyboardFocusWithin)
            tab.View.Focus();

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
        var cancellation = tab.SleepCancellation;
        tab.SleepCancellation = null;
        cancellation?.Cancel();
    }

    private async Task<bool> TryEnsureTabLoadedAsync(BrowserTab tab)
    {
        try
        {
            await EnsureTabLoadedAsync(tab);
            _ = UpdateTabFaviconAsync(tab);
            return true;
        }
        catch (OperationCanceledException ex)
        {
            if (!tab.IsClosed && !_isClosing && _activeTab == tab)
                ReportTabWakeFailure("Tab wake was cancelled", ex);

            return false;
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException or COMException or TimeoutException)
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
        tab.View.Resume();
        ApplyTabMute(tab);
        tab.View.SetVisible(CommandPaletteOverlay.Visibility != Visibility.Visible, zIndex: 10);
        RefreshPermissionBar(tab);
        RefreshFindBar(tab);
        tab.UpdateStateIndicator();
        ApplySplitLayout();
    }

    private void MoveTabToBackground(BrowserTab tab)
    {
        if (IsShownInSplit(tab))
            return;

        tab.HiddenSinceUtc = DateTime.UtcNow;
        HideTab(tab);
        if (tab.IsStartPage && !tab.View.CanGoBack && ColdUnloadTab(tab))
        {
            UpdateWorkspaceSidebar();
            return;
        }

        ApplyTabMute(tab);

        ScheduleBackgroundLifecycle(tab);
    }

    private static void HideTab(BrowserTab tab)
    {
        tab.View.SetVisible(false, zIndex: 0);
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
        UpdateSiteIndicator();
        RefreshSaveLoginBar();
    }

    private async Task CloseTabAsync(BrowserTab tab)
    {
        if (tab.IsClosed)
            return;

        if (tab.HasActiveDownload &&
            MessageBox.Show(
                $"A download is still running in \"{TrimLabel(tab.Title.Text, 48)}\".\n\nClosing the tab cancels it.",
                "Close tab?",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning) != MessageBoxResult.OK)
        {
            return;
        }

        bool wasActive = _activeTab == tab;
        int workspaceIndex = GetWorkspaceTabIndex(tab);

        string reopenAddress = SessionAddress(tab);
        if (!string.IsNullOrWhiteSpace(reopenAddress) && reopenAddress != "feather://newtab")
            _closedTabs.Push(reopenAddress);

        tab.IsClosed = true;
        if (tab == _splitLeft || tab == _splitRight)
            CloseSplitView();
        if (ReferenceEquals(_videoFullscreenTab, tab))
        {
            _videoFullscreenTab = null;
            ApplyFullscreen();
        }
        CancelSleepSchedule(tab);

        TabStrip.Children.Remove(tab.Header);
        UpdateTabStripLayout();

        DismissPermissionPrompt(tab, promptId: null);
        _findQueries.Remove(tab);
        ForgetPageDownloads(tab, tab.View);
        RemovePageControl(tab.View);

        _tabs.Remove(tab);
        foreach (BrowserTab other in _tabs)
        {
            if (ReferenceEquals(other.Opener, tab))
                other.Opener = null;
        }

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

