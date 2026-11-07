using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using FeatherBrowser.Domain.Models;
using FeatherBrowser.Features.Engine;
using FeatherBrowser.Presentation.Tabs;

namespace FeatherBrowser.Presentation.Shell;

public partial class MainWindow
{
    private async Task<BrowserTab?> AddTabAsync(
        string? address = null,
        bool select = true,
        string? workspace = null,
        bool pinned = false,
        string? titleHint = null,
        BrowserTab? openerTab = null)
    {
        if (!_engineReady || _isClosing)
            return null;

        (Border header, TextBlock title, TextBlock state, Button closeButton) = CreateTabHeader();

        bool requestedStartPage =
            string.IsNullOrWhiteSpace(address) ||
            string.Equals(address, "feather://newtab", StringComparison.OrdinalIgnoreCase);

        WebPage page = CreateWebViewControl();
        var tab = new BrowserTab
        {
            Id = Guid.NewGuid(),
            View = page,
            Header = header,
            Title = title,
            StateIndicator = state,
            IsStartPage = requestedStartPage,
            LastAddress = requestedStartPage ? string.Empty : NormalizeAddress(address!),
            Workspace = ResolveWorkspace(workspace),
            IsPinned = pinned
        };

        HookPage(tab, page);
        ConfigureInitialTabState(tab, address, titleHint);
        AttachTabHeaderEvents(tab, header, closeButton);

        int insertAt = _tabs.Count;
        if (openerTab is not null && !openerTab.IsClosed && _tabs.IndexOf(openerTab) is >= 0 and int openerIndex)
        {
            insertAt = openerIndex + 1;
            while (insertAt < _tabs.Count && ReferenceEquals(_tabs[insertAt].Opener, openerTab))
                insertAt++;
            tab.Opener = openerTab;
        }

        _tabs.Insert(insertAt, tab);
        int headerIndex = insertAt < _tabs.Count - 1 ? TabStrip.Children.IndexOf(_tabs[insertAt + 1].Header) : -1;
        if (headerIndex >= 0)
            TabStrip.Children.Insert(headerIndex, header);
        else
            TabStrip.Children.Add(header);
        UpdateTabStripLayout();

        header.Visibility = IsTabInActiveWorkspace(tab)
            ? Visibility.Visible
            : Visibility.Collapsed;

        tab.UpdateStateIndicator();
        UpdateWorkspaceSidebar();

        if (select)
        {
            await SelectTabAsync(tab);
        }
        else
        {
            UpdateResourceText();
            SaveSessionSnapshotIfEnabled();
        }

        return tab;
    }

    private string ResolveWorkspace(string? workspace)
    {
        string resolved = string.IsNullOrWhiteSpace(workspace)
            ? _store.Settings.ActiveWorkspace
            : workspace.Trim();

        EnsureWorkspaceExists(resolved);

        return _store.Settings.Workspaces.Contains(resolved, StringComparer.OrdinalIgnoreCase)
            ? resolved
            : _store.Settings.ActiveWorkspace;
    }

    private void ConfigureInitialTabState(BrowserTab tab, string? address, string? titleHint)
    {
        if (string.Equals(address, "feather://welcome", StringComparison.OrdinalIgnoreCase))
        {
            ConfigureInternalTab(tab, "Welcome");
            tab.IsWelcomePage = true;
            return;
        }

        if (string.Equals(address, "feather://settings", StringComparison.OrdinalIgnoreCase))
        {
            ConfigureInternalTab(tab, "Settings");
            tab.IsSettingsPage = true;
            return;
        }

        if (TryGetLibrarySection(address, out string librarySection))
        {
            ConfigureInternalTab(tab, LibraryTitle(librarySection));
            tab.IsLibraryPage = true;
            tab.LibrarySection = librarySection;
            return;
        }

        if (!string.IsNullOrWhiteSpace(titleHint))
        {
            tab.Title.Text = titleHint.Trim();
            return;
        }

        if (tab.IsStartPage || string.IsNullOrWhiteSpace(tab.LastAddress))
            return;

        HistoryEntry? historyEntry = _store.History.FirstOrDefault(entry =>
            string.Equals(entry.Url, tab.LastAddress, StringComparison.OrdinalIgnoreCase));

        tab.Title.Text = historyEntry is { Title.Length: > 0 }
            ? historyEntry.Title
            : SafeHost(tab.LastAddress);
    }

    private static void ConfigureInternalTab(BrowserTab tab, string title)
    {
        tab.IsStartPage = false;
        tab.LastAddress = string.Empty;
        tab.Title.Text = title;
    }

    private void SaveSessionSnapshotIfEnabled()
    {
        if (_sessionTimer.IsEnabled)
            SaveSessionSnapshot();
    }
}
