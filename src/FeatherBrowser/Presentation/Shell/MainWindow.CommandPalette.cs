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
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;

namespace FeatherBrowser.Presentation.Shell;

public partial class MainWindow : Window
{
    private void OpenCommandPalette()
    {
        CommandPaletteOverlay.Visibility = Visibility.Visible;
        CommandPaletteBox.Text = string.Empty;
        RefreshCommandPalette();
        CommandPaletteBox.Focus();
        Keyboard.Focus(CommandPaletteBox);
    }

    private void CloseCommandPalette()
    {
        CommandPaletteOverlay.Visibility = Visibility.Collapsed;
        CommandPaletteBox.Text = string.Empty;
        AddressBox.Focus();
    }

    private void RefreshCommandPalette()
    {
        string query = CommandPaletteBox.Text.Trim();
        string q = query.ToLowerInvariant();
        _paletteItems.Clear();

        void AddCommand(string label, string detail, string data)
        {
            if (q.Length == 0 || label.Contains(query, StringComparison.OrdinalIgnoreCase) || detail.Contains(query, StringComparison.OrdinalIgnoreCase))
                _paletteItems.Add(new CommandPaletteEntry { Label = label, Detail = detail, Kind = "command", Data = data });
        }

        AddCommand("New tab", "Ctrl+T", "new-tab");
        AddCommand("New private window", "Ctrl+Shift+N", "new-private");
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
        AddCommand("Toggle fullscreen", "F11", "fullscreen");
        if (_closedTabs.Count > 0)
            AddCommand("Reopen closed tab", "Ctrl+Shift+T", "reopen");

        IEnumerable<BrowserTab> tabs = _tabs.Where(t => !t.IsClosed);
        if (q.Length > 0)
            tabs = tabs.Where(t => t.Title.Text.Contains(query, StringComparison.OrdinalIgnoreCase) || DisplayAddress(t).Contains(query, StringComparison.OrdinalIgnoreCase));
        foreach (BrowserTab tab in tabs.Take(12))
        {
            _paletteItems.Add(new CommandPaletteEntry
            {
                Label = tab.Title.Text,
                Detail = $"{tab.Workspace} · {(tab == _activeTab ? "Current tab" : (tab.IsCold ? "Cold tab" : "Open tab"))}",
                Kind = "tab",
                TabId = tab.Id
            });
        }

        if (q.Length > 0)
        {
            foreach (BrowserBookmark bookmark in _store.Bookmarks.Where(x => x.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || x.Url.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(8))
                _paletteItems.Add(new CommandPaletteEntry { Label = bookmark.Title, Detail = "Favorite · " + SafeHost(bookmark.Url), Kind = "url", Data = bookmark.Url });

            foreach (HistoryEntry history in _store.History.Where(x => x.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || x.Url.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(8))
                _paletteItems.Add(new CommandPaletteEntry { Label = history.Title, Detail = "History · " + SafeHost(history.Url), Kind = "url", Data = history.Url });

            _paletteItems.Add(new CommandPaletteEntry { Label = $"Search for \"{query}\"", Detail = _store.Settings.SearchEngine, Kind = "search", Data = query });
        }

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
