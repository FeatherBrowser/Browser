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
    private async Task<BrowserTab?> AddTabAsync(string? address = null, bool select = true, string? workspace = null, bool pinned = false, string? titleHint = null)
    {
        if (_environment is null || _isClosing)
            return null;

        var title = new TextBlock
        {
            Text = "New Tab",
            Foreground = (Brush)FindResource("TextPrimary"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12.5,
            FontWeight = FontWeights.Medium,
            MaxWidth = 175
        };

        var state = new TextBlock
        {
            Text = "○",
            Foreground = _secondaryBrush,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 10,
            Width = 14,
            Margin = new Thickness(0, 0, 5, 0),
            ToolTip = "Unloaded to save memory"
        };

        var close = new Button
        {
            Content = "×",
            Style = (Style)FindResource("IconButton"),
            Background = Brushes.Transparent,
            Foreground = _secondaryBrush,
            BorderThickness = new Thickness(0),
            Width = 25,
            Height = 25,
            MinHeight = 25,
            FontSize = 14,
            Padding = new Thickness(0),
            Cursor = Cursors.Hand,
            ToolTip = "Close tab",
            Focusable = false
        };

        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.Children.Add(state);
        Grid.SetColumn(title, 1);
        headerGrid.Children.Add(title);
        Grid.SetColumn(close, 2);
        headerGrid.Children.Add(close);

        var header = new Border
        {
            Background = _inactiveTabBrush,
            CornerRadius = new CornerRadius(10),
            BorderBrush = new SolidColorBrush(Color.FromRgb(31, 42, 54)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(2, 0, 2, 0),
            Padding = new Thickness(10, 2, 4, 2),
            MinWidth = 150,
            MaxWidth = 235,
            Height = 33,
            Child = headerGrid,
            Cursor = Cursors.Hand
        };

        bool requestedStartPage = string.IsNullOrWhiteSpace(address) || string.Equals(address, "feather://newtab", StringComparison.OrdinalIgnoreCase);
        var tab = new BrowserTab
        {
            Id = Guid.NewGuid(),
            View = CreateWebViewControl(),
            Header = header,
            Title = title,
            StateIndicator = state,
            IsStartPage = requestedStartPage,
            LastAddress = requestedStartPage ? string.Empty : NormalizeAddress(address!),
            Workspace = string.IsNullOrWhiteSpace(workspace) ? _store.Settings.ActiveWorkspace : workspace.Trim(),
            IsPinned = pinned
        };

        EnsureWorkspaceExists(tab.Workspace);
        if (!_store.Settings.Workspaces.Contains(tab.Workspace, StringComparer.OrdinalIgnoreCase))
            tab.Workspace = _store.Settings.ActiveWorkspace;
        if (!string.IsNullOrWhiteSpace(titleHint))
            tab.Title.Text = titleHint.Trim();
        else if (!tab.IsStartPage && !string.IsNullOrWhiteSpace(tab.LastAddress))
        {
            HistoryEntry? known = _store.History.FirstOrDefault(x => string.Equals(x.Url, tab.LastAddress, StringComparison.OrdinalIgnoreCase));
            tab.Title.Text = known is not null && !string.IsNullOrWhiteSpace(known.Title) ? known.Title : SafeHost(tab.LastAddress);
        }

        if (string.Equals(address, "feather://welcome", StringComparison.OrdinalIgnoreCase))
        {
            tab.IsStartPage = false;
            tab.IsWelcomePage = true;
            tab.LastAddress = string.Empty;
            tab.Title.Text = "Welcome";
        }
        else if (string.Equals(address, "feather://settings", StringComparison.OrdinalIgnoreCase))
        {
            tab.IsStartPage = false;
            tab.IsSettingsPage = true;
            tab.LastAddress = string.Empty;
            tab.Title.Text = "Settings";
        }
        else if (TryGetLibrarySection(address, out string librarySection))
        {
            tab.IsStartPage = false;
            tab.IsLibraryPage = true;
            tab.LibrarySection = librarySection;
            tab.LastAddress = string.Empty;
            tab.Title.Text = LibraryTitle(librarySection);
        }

        header.MouseLeftButtonDown += async (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Middle)
            {
                await CloseTabAsync(tab);
                e.Handled = true;
                return;
            }

            if (!close.IsMouseOver)
                await SelectTabAsync(tab);
        };
        header.MouseRightButtonUp += (_, _) => BuildTabContextMenu(tab).IsOpen = true;
        close.Click += async (_, _) => await CloseTabAsync(tab);

        TabStrip.Children.Add(header);
        _tabs.Add(tab);
        header.Visibility = IsTabInActiveWorkspace(tab) ? Visibility.Visible : Visibility.Collapsed;
        tab.UpdateStateIndicator();
        UpdateWorkspaceSidebar();

        if (select)
            await SelectTabAsync(tab);

        UpdateResourceText();
        if (_sessionTimer.IsEnabled)
            SaveSessionSnapshot();
        return tab;
    }

    private async Task SelectTabAsync(BrowserTab tab)
    {
        if (tab.IsClosed || (_activeTab == tab && tab.IsLoaded))
            return;

        if (!string.Equals(tab.Workspace, _store.Settings.ActiveWorkspace, StringComparison.OrdinalIgnoreCase))
        {
            _store.Settings.ActiveWorkspace = tab.Workspace;
            _store.SaveSettings();
            RefreshWorkspaceTabVisibility();
            UpdateWorkspaceSidebar();
        }

        BrowserTab? previous = _activeTab;
        _activeTab = tab;

        foreach (BrowserTab item in _tabs)
            item.SetSelected(item == tab, _activeTabBrush, _inactiveTabBrush, _accentBrush);

        tab.SleepCancellation?.Cancel();
        tab.SleepCancellation?.Dispose();
        tab.SleepCancellation = null;
        tab.LastActivatedUtc = DateTime.UtcNow;

        try
        {
            await EnsureTabLoadedAsync(tab);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Could not wake this tab";
            MessageBox.Show($"Feather could not recreate this tab.\n\n{ex.Message}", "Feather tab", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (tab.IsClosed || _isClosing) return;
        if (tab != _activeTab)
        {
            tab.View.Visibility = Visibility.Collapsed;
            ScheduleBackgroundLifecycle(tab);
            return;
        }
        if (tab.View.CoreWebView2.IsSuspended)
            tab.View.CoreWebView2.Resume();
        if (_store.Settings.MuteBackgroundTabs || _store.Settings.GameMode)
            tab.View.CoreWebView2.IsMuted = false;

        tab.View.Visibility = Visibility.Visible;
        Panel.SetZIndex(tab.View, 10);

        if (previous is not null && previous != tab && !previous.IsClosed && previous.IsLoaded)
        {
            previous.HiddenSinceUtc = DateTime.UtcNow;
            previous.View.Visibility = Visibility.Collapsed;
            Panel.SetZIndex(previous.View, 0);
            if (_store.Settings.MuteBackgroundTabs || _store.Settings.GameMode)
                previous.View.CoreWebView2.IsMuted = true;
            ScheduleBackgroundLifecycle(previous);
        }

        AddressBox.Text = DisplayAddress(tab);
        Title = $"{tab.Title.Text} — Feather";
        TitleText.Text = tab.Title.Text;
        UpdateNavigationButtons();
        UpdateBookmarkButton();
        UpdateFeatureButtons();
        EnforceLoadedTabBudget();
        UpdateWorkspaceSidebar();
        UpdateResourceText();
        if (_sessionTimer.IsEnabled)
            SaveSessionSnapshot();
    }

    private async Task CloseTabAsync(BrowserTab tab)
    {
        if (tab.IsClosed)
            return;

        int index = _tabs.IndexOf(tab);
        bool wasActive = _activeTab == tab;

        if (!string.IsNullOrWhiteSpace(tab.LastAddress))
            _closedTabs.Push(tab.LastAddress);

        tab.IsClosed = true;
        tab.SleepCancellation?.Cancel();
        tab.SleepCancellation?.Dispose();
        tab.SleepCancellation = null;

        TabStrip.Children.Remove(tab.Header);
        if (tab.IsLoaded)
            BrowserHost.Children.Remove(tab.View);
        _tabs.Remove(tab);
        tab.View.Dispose();
        UpdateWorkspaceSidebar();

        if (!wasActive)
        {
            UpdateResourceText();
            if (_sessionTimer.IsEnabled)
                SaveSessionSnapshot();
            return;
        }

        _activeTab = null;
        if (_tabs.Count == 0)
        {
            await AddTabAsync();
            return;
        }

        List<BrowserTab> workspaceTabs = _tabs.Where(t => IsTabInActiveWorkspace(t)).ToList();
        if (workspaceTabs.Count == 0)
        {
            await AddTabAsync(workspace: _store.Settings.ActiveWorkspace);
            return;
        }

        int next = Math.Clamp(Math.Min(index, workspaceTabs.Count - 1), 0, workspaceTabs.Count - 1);
        await SelectTabAsync(workspaceTabs[next]);
        if (_sessionTimer.IsEnabled)
            SaveSessionSnapshot();

    }
}
