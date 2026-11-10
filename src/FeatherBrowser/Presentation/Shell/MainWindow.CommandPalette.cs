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
using FeatherBrowser.Presentation.Commands;
using FeatherBrowser.Presentation.Tabs;
using Microsoft.Win32;

namespace FeatherBrowser.Presentation.Shell;

public partial class MainWindow : Window
{
    private bool _paletteTabsOnly;

    private void OpenCommandPalette(bool tabsOnly = false)
    {
        _paletteTabsOnly = tabsOnly;
        System.Windows.Automation.AutomationProperties.SetName(
            CommandPaletteBox,
            tabsOnly ? "Search open tabs" : "Search tabs, history, favorites and commands");

        if (_activeTab is { IsLoaded: true } active)
            active.View.SetVisible(false, zIndex: 0);
        HideSplitPartner();

        CommandPaletteOverlay.Visibility = Visibility.Visible;
        CommandPaletteBox.Text = string.Empty;
        RefreshCommandPalette();
        CommandPaletteBox.Focus();
        Keyboard.Focus(CommandPaletteBox);
    }

    private void CloseCommandPalette()
    {
        bool wasOpen = CommandPaletteOverlay.Visibility == Visibility.Visible;
        CommandPaletteOverlay.Visibility = Visibility.Collapsed;
        _paletteTabsOnly = false;
        CommandPaletteBox.Text = string.Empty;

        if (wasOpen && _activeTab is { IsLoaded: true } active && WindowState != WindowState.Minimized)
            active.View.SetVisible(true, zIndex: 10);
        if (wasOpen)
            ApplySplitLayout();

        AddressBox.Focus();
    }

    private void RefreshCommandPalette()
    {
        string query = CommandPaletteBox.Text.Trim();
        string q = query.ToLowerInvariant();
        _paletteItems.Clear();

        if (_paletteTabsOnly)
        {
            AddTabEntries(query, limit: 500, mostRecentFirst: true);
            ShowPaletteItems();
            return;
        }

        void AddCommand(string label, string detail, string data)
        {
            if (q.Length == 0 || label.Contains(query, StringComparison.OrdinalIgnoreCase) || detail.Contains(query, StringComparison.OrdinalIgnoreCase))
                _paletteItems.Add(new CommandPaletteEntry { Label = label, Detail = detail, Kind = "command", Data = data });
        }

        AddCommand("New tab", "Ctrl+T", "new-tab");
        AddCommand("New private window", "Ctrl+Shift+N", "new-private");
        AddCommand("Search open tabs", "Ctrl+Shift+A", "search-tabs");
        AddCommand("Toggle workspace sidebar", "Ctrl+B", "toggle-sidebar");
        foreach (string workspace in _store.Settings.Workspaces)
            AddCommand($"Switch workspace: {workspace}", workspace == _store.Settings.ActiveWorkspace ? "Current" : "Workspace", "workspace:" + workspace);
        AddCommand("Trim browser memory", "Ctrl+Shift+M", "trim-memory");
        AddCommand("Performance Center", "Shift+Esc", "performance");
        AddCommand(_store.Settings.GameMode ? "Disable Gaming Mode" : "Enable Gaming Mode", "Ctrl+Shift+G", "toggle-game");
        AddCommand(_shieldEnabled ? "Disable Feather Shield" : "Enable Feather Shield", "Privacy", "toggle-shield");
        AddCommand("Settings", "Ctrl+,", "settings");
        AddCommand("History", "Ctrl+H", "history");
        AddCommand("Downloads", "Ctrl+J", "downloads");
        AddCommand("Favorites", "Ctrl+Shift+O", "favorites");
        AddCommand("Reload current tab", "Ctrl+R", "reload");
        AddCommand("Picture-in-picture video", "Video", "pip");
        if (_activeTab is not null && SplitPartner(_activeTab) is not null)
            AddCommand("Exit split view", "Split view", "exit-split");
        AddCommand(_activeTab?.IsReaderView == true ? "Exit reader view" : "Reader view", "Page", "reader");
        AddCommand("Toggle fullscreen", "F11", "fullscreen");
        if (_closedTabs.Count > 0)
            AddCommand("Reopen closed tab", "Ctrl+Shift+T", "reopen");

        AddTabEntries(query, limit: 12, mostRecentFirst: false);

        if (q.Length > 0)
        {
            foreach (BrowserBookmark bookmark in _store.Bookmarks.Where(x => x.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || x.Url.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(8))
                _paletteItems.Add(new CommandPaletteEntry { Label = bookmark.Title, Detail = "Favorite · " + SafeHost(bookmark.Url), Kind = "url", Data = bookmark.Url });

            foreach (HistoryEntry history in _store.History.Where(x => x.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || x.Url.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(8))
                _paletteItems.Add(new CommandPaletteEntry { Label = history.Title, Detail = "History · " + SafeHost(history.Url), Kind = "url", Data = history.Url });

            _paletteItems.Add(new CommandPaletteEntry { Label = $"Search for \"{query}\"", Detail = _store.Settings.SearchEngine, Kind = "search", Data = query });
        }

        ShowPaletteItems();
    }

    private void AddTabEntries(string query, int limit, bool mostRecentFirst)
    {
        IEnumerable<BrowserTab> tabs = _tabs.Where(t => !t.IsClosed);
        if (query.Length > 0)
            tabs = tabs.Where(t => t.Title.Text.Contains(query, StringComparison.OrdinalIgnoreCase) || DisplayAddress(t).Contains(query, StringComparison.OrdinalIgnoreCase));
        if (mostRecentFirst)
            tabs = tabs.OrderByDescending(t => t == _activeTab).ThenByDescending(t => t.LastActivatedUtc);

        foreach (BrowserTab tab in tabs.Take(limit))
        {
            string state = tab == _activeTab ? "Current tab" : tab.IsCold ? "Cold tab" : "Open tab";
            string host = tab.IsInternalPage ? "Feather" : SafeHost(tab.LastAddress);
            _paletteItems.Add(new CommandPaletteEntry
            {
                Label = tab.Title.Text,
                Detail = $"{host} · {tab.Workspace} · {state}",
                Kind = "tab",
                TabId = tab.Id
            });
        }
    }

    private void ShowPaletteItems()
    {
        CommandPaletteList.ItemsSource = null;
        CommandPaletteList.ItemsSource = _paletteItems;
        if (_paletteItems.Count > 0)
            CommandPaletteList.SelectedIndex = 0;
    }

    private async Task ExecutePaletteEntryAsync(CommandPaletteEntry? entry)
    {
        if (entry is null)
            return;

        CloseCommandPalette();
        if (entry.Kind == "tab" && entry.TabId is Guid tabId)
        {
            BrowserTab? tab = _tabs.FirstOrDefault(t => t.Id == tabId && !t.IsClosed);
            if (tab is not null)
                await SelectTabAsync(tab);
            return;
        }
        if (entry.Kind is "url" or "search")
        {
            if (_activeTab is null)
                await AddTabAsync(entry.Data);
            else
                Navigate(_activeTab, entry.Data);
            return;
        }

        if (entry.Data.StartsWith("workspace:", StringComparison.Ordinal))
        {
            await SwitchWorkspaceAsync(entry.Data[10..]);
            return;
        }

        switch (entry.Data)
        {
            case "new-tab": await AddTabAsync(); break;
            case "search-tabs": OpenCommandPalette(tabsOnly: true); break;
            case "pip": await TogglePictureInPictureAsync(); break;
            case "exit-split": CloseSplitView(); break;
            case "reader": await ToggleReaderViewAsync(); break;
            case "new-private": OpenPrivateWindow(); break;
            case "toggle-sidebar":
                _store.Settings.ShowWorkspaceSidebar = !_store.Settings.ShowWorkspaceSidebar;
                _store.SaveSettings();
                ApplyUiPreferences();
                UpdateWorkspaceSidebar();
                break;
            case "trim-memory": TrimMemoryNow(); break;
            case "performance": ShowPerformanceCenter(); break;
            case "toggle-game": ToggleGameMode(); break;
            case "toggle-shield":
                _shieldEnabled = !_shieldEnabled;
                _store.Settings.ShieldEnabled = _shieldEnabled;
                _store.SaveSettings();
                UpdateFeatureButtons();
                StatusText.Text = _shieldEnabled ? "Feather Shield enabled" : "Feather Shield disabled";
                break;
            case "settings": await OpenSettingsAsync(); break;
            case "history": await OpenLibraryAsync("history"); break;
            case "downloads": await OpenLibraryAsync("downloads"); break;
            case "favorites": await OpenLibraryAsync("favorites"); break;
            case "reload": ReloadButton_Click(this, new RoutedEventArgs()); break;
            case "fullscreen": ToggleFullscreen(); break;
            case "reopen": if (_closedTabs.Count > 0) await AddTabAsync(_closedTabs.Pop()); break;
        }
    }

    private void CommandPaletteBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (CommandPaletteList is null || CommandPaletteBox is null)
            return;
        RefreshCommandPalette();
    }

    private async void CommandPaletteBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CloseCommandPalette();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Down && CommandPaletteList.Items.Count > 0)
        {
            CommandPaletteList.SelectedIndex = Math.Min(CommandPaletteList.Items.Count - 1, CommandPaletteList.SelectedIndex + 1);
            CommandPaletteList.ScrollIntoView(CommandPaletteList.SelectedItem);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Up && CommandPaletteList.Items.Count > 0)
        {
            CommandPaletteList.SelectedIndex = Math.Max(0, CommandPaletteList.SelectedIndex - 1);
            CommandPaletteList.ScrollIntoView(CommandPaletteList.SelectedItem);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Enter)
        {
            await ExecutePaletteEntryAsync(CommandPaletteList.SelectedItem as CommandPaletteEntry);
            e.Handled = true;
        }
    }

    private async void CommandPaletteList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        await ExecutePaletteEntryAsync(CommandPaletteList.SelectedItem as CommandPaletteEntry);
    }

    private void CommandPaletteOverlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.Source, sender))
            CloseCommandPalette();
    }
}
