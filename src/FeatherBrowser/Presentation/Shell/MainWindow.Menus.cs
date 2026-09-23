using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Controls.Primitives;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using System.Windows;
using FeatherBrowser.Domain.Models;
using FeatherBrowser.Presentation.Tabs;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;

namespace FeatherBrowser.Presentation.Shell;

public partial class MainWindow : Window
{
    private void OpenPrivateWindow(string? address = null)
    {
        var window = new MainWindow(address, privateMode: true);
        window.Show();
    }

    private void MenuButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = MenuButton,
            Placement = PlacementMode.Bottom,
            MinWidth = 260
        };

        var palette = new MenuItem { Header = "Command palette", InputGestureText = "Ctrl+K" };
        palette.Click += (_, _) => OpenCommandPalette();
        menu.Items.Add(palette);

        var newTab = new MenuItem { Header = "New tab", InputGestureText = "Ctrl+T" };
        newTab.Click += async (_, _) => await AddTabAsync();
        menu.Items.Add(newTab);

        var privateWindow = new MenuItem { Header = "New private window", InputGestureText = "Ctrl+Shift+N" };
        privateWindow.Click += (_, _) => OpenPrivateWindow();
        menu.Items.Add(privateWindow);

        var workspaces = new MenuItem { Header = $"Workspaces · {_store.Settings.ActiveWorkspace}" };
        foreach (string workspace in _store.Settings.Workspaces)
        {
            var item = new MenuItem { Header = workspace, IsCheckable = true, IsChecked = string.Equals(workspace, _store.Settings.ActiveWorkspace, StringComparison.OrdinalIgnoreCase) };
            item.Click += async (_, _) => await SwitchWorkspaceAsync(workspace);
            workspaces.Items.Add(item);
        }
        workspaces.Items.Add(new Separator());
        var toggleSidebar = new MenuItem { Header = "Show workspace sidebar", IsCheckable = true, IsChecked = _store.Settings.ShowWorkspaceSidebar };
        toggleSidebar.Click += (_, _) =>
        {
            _store.Settings.ShowWorkspaceSidebar = !_store.Settings.ShowWorkspaceSidebar;
            _store.SaveSettings();
            ApplyUiPreferences();
        };
        workspaces.Items.Add(toggleSidebar);
        menu.Items.Add(workspaces);

        menu.Items.Add(BuildFavoritesMenu());
        menu.Items.Add(BuildHistoryMenu());

        var downloads = new MenuItem { Header = "Downloads", InputGestureText = "Ctrl+J" };
        downloads.Click += async (_, _) => await OpenLibraryAsync("downloads");
        menu.Items.Add(downloads);

        var game = new MenuItem
        {
            Header = "Gaming Mode", InputGestureText = "Ctrl+Shift+G",
            IsCheckable = true,
            IsChecked = _store.Settings.GameMode
        };
        game.Click += (_, _) => ToggleGameMode();
        menu.Items.Add(game);

        var trimMemory = new MenuItem { Header = "Trim background memory now", InputGestureText = "Ctrl+Shift+M" };
        trimMemory.Click += (_, _) => TrimMemoryNow();
        menu.Items.Add(trimMemory);

        var performance = new MenuItem { Header = "Performance Center", InputGestureText = "Shift+Esc" };
        performance.Click += (_, _) => ShowPerformanceCenter();
        menu.Items.Add(performance);
        menu.Items.Add(new Separator());

        var search = new MenuItem { Header = $"Search engine · {_store.Settings.SearchEngine}" };
        foreach (string engine in new[] { "Google", "Bing", "DuckDuckGo" })
        {
            var item = new MenuItem { Header = engine, IsCheckable = true, IsChecked = _store.Settings.SearchEngine == engine };
            item.Click += (_, _) => SetSearchEngine(engine);
            search.Items.Add(item);
        }
        menu.Items.Add(search);

        var restore = new MenuItem
        {
            Header = "Restore workspaces on startup",
            IsCheckable = true,
            IsChecked = string.Equals(_store.Settings.StartupMode, "Restore", StringComparison.OrdinalIgnoreCase)
        };
        restore.Click += (_, _) =>
        {
            bool restoreEnabled = !string.Equals(_store.Settings.StartupMode, "Restore", StringComparison.OrdinalIgnoreCase);
            _store.Settings.StartupMode = restoreEnabled ? "Restore" : "NewTab";
            _store.Settings.RestorePreviousSession = restoreEnabled;
            _store.SaveSettings();
        };
        menu.Items.Add(restore);

        menu.Items.Add(new Separator());

        var import = new MenuItem { Header = "Import favorites + history + tabs from Edge" };
        import.Click += async (_, _) => await ImportFromEdgeAsync();
        menu.Items.Add(import);

        var passwords = new MenuItem { Header = $"Passwords ({_passwordVault.Count})" };
        var fillPassword = new MenuItem { Header = "Autofill saved login", InputGestureText = "Ctrl+Shift+P" };
        fillPassword.Click += async (_, _) => await AutofillSavedPasswordAsync();
        passwords.Items.Add(fillPassword);

        var importPasswords = new MenuItem { Header = "Import passwords from Edge CSV…" };
        importPasswords.Click += (_, _) => ImportPasswordsFromEdgeCsv();
        passwords.Items.Add(importPasswords);

        var edgeExport = new MenuItem { Header = "Open Edge password export" };
        edgeExport.Click += (_, _) => OpenEdgePasswordExport();
        passwords.Items.Add(edgeExport);

        passwords.Items.Add(new Separator());
        var clearPasswords = new MenuItem { Header = "Forget imported passwords" };
        clearPasswords.Click += (_, _) => ClearImportedPasswords();
        passwords.Items.Add(clearPasswords);
        menu.Items.Add(passwords);

        var defaults = new MenuItem { Header = "Make Feather the default browser" };
        defaults.Click += (_, _) => RegisterAsDefaultBrowser();
        menu.Items.Add(defaults);

        menu.Items.Add(new Separator());

        var settingsPage = new MenuItem { Header = "Settings", InputGestureText = "Ctrl+," };
        settingsPage.Click += async (_, _) => await OpenSettingsAsync();
        menu.Items.Add(settingsPage);

        var clearHistory = new MenuItem { Header = "Clear Feather history" };
        clearHistory.Click += (_, _) => ClearHistory();
        menu.Items.Add(clearHistory);

        var about = new MenuItem { Header = "About Feather" };
        about.Click += (_, _) => MessageBox.Show(
            $"{FeatherBrowser.Infrastructure.Resources.AppInfo.DisplayName}\n\nA Windows browser built with WPF and Microsoft WebView2.\n\nFeather WebView3 is our resource-management layer over WebView2. It is not a separate rendering engine.\n\nFeather source is available under the MIT License. Third-party components retain their own terms.",
            "About Feather",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        menu.Items.Add(about);

        menu.IsOpen = true;
    }

    private ContextMenu BuildTabContextMenu(BrowserTab tab)
    {
        var menu = new ContextMenu { PlacementTarget = tab.Header, Placement = PlacementMode.MousePoint, MinWidth = 210 };

        var pin = new MenuItem { Header = tab.IsPinned ? "Unpin tab" : "Pin tab", IsCheckable = true, IsChecked = tab.IsPinned };
        pin.Click += (_, _) =>
        {
            tab.IsPinned = !tab.IsPinned;
            tab.UpdateStateIndicator();
            ApplyPerformanceToTabs();
        };
        menu.Items.Add(pin);

        string tabHost = tab.IsInternalPage ? string.Empty : SafeHost(tab.LastAddress);
        if (!string.IsNullOrWhiteSpace(tabHost) && tabHost != "page")
        {
            bool keepAlive = _store.Settings.KeepAliveSites.Any(x =>
            {
                string host = x.Trim().TrimStart('.');
                return tabHost.Equals(host, StringComparison.OrdinalIgnoreCase) || tabHost.EndsWith('.' + host, StringComparison.OrdinalIgnoreCase);
            });
            var keepSiteAlive = new MenuItem { Header = "Keep this site alive in background", IsCheckable = true, IsChecked = keepAlive };
            keepSiteAlive.Click += (_, _) =>
            {
                if (keepAlive)
                    _store.Settings.KeepAliveSites.RemoveAll(x => tabHost.Equals(x.Trim().TrimStart('.'), StringComparison.OrdinalIgnoreCase));
                else if (!_store.Settings.KeepAliveSites.Contains(tabHost, StringComparer.OrdinalIgnoreCase))
                    _store.Settings.KeepAliveSites.Add(tabHost);
                _store.SaveSettings();
                if (!keepAlive && tab != _activeTab && tab.IsLoaded)
                    ScheduleBackgroundLifecycle(tab);
            };
            menu.Items.Add(keepSiteAlive);
        }

        var duplicate = new MenuItem { Header = "Duplicate tab" };
        duplicate.Click += async (_, _) => await AddTabAsync(
            tab.IsSettingsPage ? "feather://settings" :
            tab.IsLibraryPage ? $"feather://{tab.LibrarySection}" :
            tab.IsStartPage ? null : tab.LastAddress);
        menu.Items.Add(duplicate);

        var moveWorkspace = new MenuItem { Header = "Move to workspace" };
        foreach (string workspace in _store.Settings.Workspaces)
        {
            var targetWorkspace = new MenuItem
            {
                Header = workspace,
                IsCheckable = true,
                IsChecked = string.Equals(tab.Workspace, workspace, StringComparison.OrdinalIgnoreCase)
            };
            targetWorkspace.Click += async (_, _) =>
            {
                tab.Workspace = workspace;
                _store.SaveSettings();
                RefreshWorkspaceTabVisibility();
                UpdateWorkspaceSidebar();
                SaveSessionSnapshot();
                if (tab == _activeTab)
                    await SwitchWorkspaceAsync(workspace);
                else if (_store.Settings.ColdUnloadOtherWorkspaces && tab.IsLoaded)
                    HibernateTab(tab);
            };
            moveWorkspace.Items.Add(targetWorkspace);
        }
        menu.Items.Add(moveWorkspace);

        var unload = new MenuItem { Header = tab.IsLoaded ? "Unload tab to save memory" : "Tab already unloaded", IsEnabled = tab.IsLoaded && tab != _activeTab && !tab.IsPinned };
        unload.Click += (_, _) => ColdUnloadTab(tab);
        menu.Items.Add(unload);

        menu.Items.Add(new Separator());
        var closeOthers = new MenuItem { Header = "Close other tabs" };
        closeOthers.Click += async (_, _) =>
        {
            foreach (BrowserTab other in _tabs.Where(t => t != tab).ToList())
                await CloseTabAsync(other);
        };
        menu.Items.Add(closeOthers);

        var close = new MenuItem { Header = "Close tab" };
        close.Click += async (_, _) => await CloseTabAsync(tab);
        menu.Items.Add(close);
        return menu;
    }

    private MenuItem BuildFavoritesMenu()
    {
        var root = new MenuItem { Header = $"Favorites ({_store.Bookmarks.Count})" };
        var manager = new MenuItem { Header = "Open Favorites manager", InputGestureText = "Ctrl+Shift+O" };
        manager.Click += async (_, _) => await OpenLibraryAsync("favorites");
        root.Items.Add(manager);
        root.Items.Add(new Separator());

        if (_store.Bookmarks.Count == 0)
        {
            root.Items.Add(new MenuItem { Header = "No favorites yet", IsEnabled = false });
            return root;
        }

        foreach (IGrouping<string, BrowserBookmark> group in _store.Bookmarks
                     .OrderBy(x => x.Folder)
                     .ThenBy(x => x.Title)
                     .Take(150)
                     .GroupBy(x => string.IsNullOrWhiteSpace(x.Folder) ? "Favorites" : x.Folder))
        {
            var folder = new MenuItem { Header = group.Key };
            foreach (BrowserBookmark bookmark in group.Take(40))
            {
                var item = new MenuItem { Header = TrimLabel(bookmark.Title, 48), ToolTip = bookmark.Url };
                item.Click += (_, _) => NavigateCurrent(bookmark.Url);
                folder.Items.Add(item);
            }
            root.Items.Add(folder);
        }
        return root;
    }

    private MenuItem BuildHistoryMenu()
    {
        var root = new MenuItem { Header = "History", InputGestureText = "Ctrl+H" };
        var manager = new MenuItem { Header = "Open full history" };
        manager.Click += async (_, _) => await OpenLibraryAsync("history");
        root.Items.Add(manager);
        root.Items.Add(new Separator());

        if (_store.History.Count == 0)
        {
            root.Items.Add(new MenuItem { Header = "No history yet", IsEnabled = false });
            return root;
        }

        foreach (HistoryEntry entry in _store.History.Take(40))
        {
            string time = entry.LastVisited.LocalDateTime.ToString("dd MMM HH:mm");
            var item = new MenuItem { Header = $"{TrimLabel(entry.Title, 38)}   {time}", ToolTip = entry.Url };
            item.Click += (_, _) => NavigateCurrent(entry.Url);
            root.Items.Add(item);
        }
        return root;
    }

    private void ClearHistory()
    {
        if (MessageBox.Show("Clear Feather's local browsing history?", "Clear history", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        _store.ClearHistory();
        StatusText.Text = "Feather history cleared";
    }
}
