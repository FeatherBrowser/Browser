using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using FeatherBrowser.Domain.Models;
using FeatherBrowser.Presentation.Tabs;
using Microsoft.Web.WebView2.Core;

namespace FeatherBrowser.Presentation.Shell;

public partial class MainWindow : Window
{
    private sealed class TabHeaderVisuals
    {
        public required Image Favicon { get; init; }
        public required FrameworkElement FallbackIcon { get; init; }
        public CoreWebView2? FaviconCore { get; set; }
    }

    private static readonly Brush TabActiveBrush =
        new SolidColorBrush(Color.FromRgb(40, 60, 84));

    private static readonly Brush TabInactiveBrush =
        new SolidColorBrush(Color.FromRgb(10, 20, 34));

    private static readonly Brush TabHoverBrush =
        new SolidColorBrush(Color.FromRgb(20, 37, 55));

    private static readonly Brush TabHoverBorderBrush =
        new SolidColorBrush(Color.FromRgb(49, 74, 99));

    private static readonly Brush TabTitleBrush =
        new SolidColorBrush(Color.FromRgb(226, 232, 240));

    private static readonly Brush TabMutedTitleBrush =
        new SolidColorBrush(Color.FromRgb(156, 166, 181));

    private async Task<BrowserTab?> AddTabAsync(
        string? address = null,
        bool select = true,
        string? workspace = null,
        bool pinned = false,
        string? titleHint = null)
    {
        if (_environment is null || _isClosing)
            return null;

        (Border header, TextBlock title, TextBlock state, Button closeButton) = CreateTabHeader();

        bool requestedStartPage =
            string.IsNullOrWhiteSpace(address) ||
            string.Equals(address, "feather://newtab", StringComparison.OrdinalIgnoreCase);

        var tab = new BrowserTab
        {
            Id = Guid.NewGuid(),
            View = CreateWebViewControl(),
            Header = header,
            Title = title,
            StateIndicator = state,
            IsStartPage = requestedStartPage,
            LastAddress = requestedStartPage ? string.Empty : NormalizeAddress(address!),
            Workspace = ResolveWorkspace(workspace),
            IsPinned = pinned
        };

        ConfigureInitialTabState(tab, address, titleHint);
        AttachTabHeaderEvents(tab, header, closeButton);

        TabStrip.Children.Add(header);
        _tabs.Add(tab);
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

    private (Border Header, TextBlock Title, TextBlock State, Button CloseButton) CreateTabHeader()
    {
        var favicon = new Image
        {
            Width = 16,
            Height = 16,
            VerticalAlignment = VerticalAlignment.Center,
            Stretch = Stretch.Uniform,
            SnapsToDevicePixels = true
        };

        FrameworkElement fallbackIcon = CreateDefaultTabIcon();

        var iconHost = new Grid
        {
            Width = 16,
            Height = 16,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        iconHost.Children.Add(fallbackIcon);
        iconHost.Children.Add(favicon);

        var title = new TextBlock
        {
            Text = "New Tab",
            Foreground = TabTitleBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12.5,
            FontWeight = FontWeights.Normal,
            MaxWidth = 160
        };

        var state = new TextBlock
        {
            Text = "○",
            Visibility = Visibility.Collapsed,
            Width = 0,
            Height = 0,
            IsHitTestVisible = false
        };

        var closeButton = new Button
        {
            Content = "×",
            Style = (Style)FindResource("IconButton"),
            Background = Brushes.Transparent,
            Foreground = TabMutedTitleBrush,
            BorderThickness = new Thickness(0),
            Width = 26,
            Height = 26,
            MinWidth = 26,
            MinHeight = 26,
            FontSize = 16,
            FontWeight = FontWeights.Light,
            Padding = new Thickness(0, 0, 0, 2),
            Margin = new Thickness(5, 0, 0, 0),
            Cursor = Cursors.Hand,
            ToolTip = "Close tab",
            Focusable = false
        };

        var content = new Grid
        {
            VerticalAlignment = VerticalAlignment.Stretch
        };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        content.Children.Add(iconHost);

        Grid.SetColumn(title, 1);
        content.Children.Add(title);

        Grid.SetColumn(closeButton, 2);
        content.Children.Add(closeButton);

        var header = new Border
        {
            Background = TabInactiveBrush,
            CornerRadius = new CornerRadius(9, 9, 0, 0),
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Margin = new Thickness(1, 4, 1, 0),
            Padding = new Thickness(11, 0, 5, 0),
            MinWidth = 140,
            MaxWidth = 220,
            Height = 36,
            Child = content,
            Cursor = Cursors.Hand,
            SnapsToDevicePixels = true,
            Tag = new TabHeaderVisuals
            {
                Favicon = favicon,
                FallbackIcon = fallbackIcon
            }
        };

        header.MouseEnter += (_, _) =>
        {
            if (_activeTab?.Header != header)
            {
                header.Background = TabHoverBrush;
                header.BorderBrush = TabHoverBorderBrush;
            }
        };

        header.MouseLeave += (_, _) =>
        {
            if (_activeTab?.Header != header)
            {
                header.Background = TabInactiveBrush;
                header.BorderBrush = Brushes.Transparent;
            }
        };

        closeButton.MouseEnter += (_, _) =>
            closeButton.Foreground = Brushes.White;

        closeButton.MouseLeave += (_, _) =>
            closeButton.Foreground = TabMutedTitleBrush;

        return (header, title, state, closeButton);
    }

    private static FrameworkElement CreateDefaultTabIcon()
    {
        var icon = new Grid
        {
            Width = 16,
            Height = 16,
            IsHitTestVisible = false
        };

        var outline = new Ellipse
        {
            Width = 13,
            Height = 13,
            Stroke = TabMutedTitleBrush,
            StrokeThickness = 1.25,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var vertical = new Ellipse
        {
            Width = 6,
            Height = 13,
            Stroke = TabMutedTitleBrush,
            StrokeThickness = 1,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var horizontal = new Line
        {
            X1 = 2,
            X2 = 14,
            Y1 = 8,
            Y2 = 8,
            Stroke = TabMutedTitleBrush,
            StrokeThickness = 1,
            SnapsToDevicePixels = true
        };

        icon.Children.Add(outline);
        icon.Children.Add(vertical);
        icon.Children.Add(horizontal);
        return icon;
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

    private void AttachTabHeaderEvents(BrowserTab tab, Border header, Button closeButton)
    {
        header.MouseLeftButtonDown += async (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Middle)
            {
                e.Handled = true;
                await CloseTabAsync(tab);
                return;
            }

            if (!closeButton.IsMouseOver)
                await SelectTabAsync(tab);
        };

        header.MouseRightButtonUp += (_, _) =>
            BuildTabContextMenu(tab).IsOpen = true;

        closeButton.Click += async (_, _) =>
            await CloseTabAsync(tab);
    }

    private async Task SelectTabAsync(BrowserTab tab)
    {
        if (tab.IsClosed || (_activeTab == tab && tab.IsLoaded))
            return;

        ActivateWorkspaceFor(tab);

        BrowserTab? previous = _activeTab;
        _activeTab = tab;

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

    private void UpdateTabSelection(BrowserTab selectedTab)
    {
        foreach (BrowserTab item in _tabs)
        {
            bool selected = item == selectedTab;

            item.SetSelected(
                selected,
                TabActiveBrush,
                TabInactiveBrush,
                _accentBrush);

            item.Header.Background = selected ? TabActiveBrush : TabInactiveBrush;
            item.Header.BorderBrush = Brushes.Transparent;
            item.Header.BorderThickness = new Thickness(0);
            item.Title.Foreground = selected ? TabTitleBrush : TabMutedTitleBrush;
        }
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
        catch (ObjectDisposedException ex)
        {
            ReportTabWakeFailure("Tab wake failed because the WebView was disposed", ex);
            return false;
        }
        catch (InvalidOperationException ex)
        {
            ReportTabWakeFailure("Tab wake failed due to an invalid WebView state", ex);
            return false;
        }
        catch (COMException ex)
        {
            ReportTabWakeFailure("Tab wake failed due to a WebView2 COM error", ex);
            return false;
        }
    }

    private void HookTabFavicon(BrowserTab tab)
    {
        if (tab.IsClosed || !tab.IsLoaded || tab.View.CoreWebView2 is null)
            return;

        if (tab.Header.Tag is not TabHeaderVisuals visuals)
            return;

        CoreWebView2 core = tab.View.CoreWebView2;
        if (ReferenceEquals(visuals.FaviconCore, core))
            return;

        visuals.FaviconCore = core;
        core.FaviconChanged += async (_, _) => await UpdateTabFaviconAsync(tab);
        core.NavigationCompleted += async (_, _) => await UpdateTabFaviconAsync(tab);
    }

    private static void ShowFallbackIcon(TabHeaderVisuals visuals)
    {
        visuals.Favicon.Source = null;
        visuals.Favicon.Visibility = Visibility.Collapsed;
        visuals.FallbackIcon.Visibility = Visibility.Visible;
    }

    private static void ShowFavicon(TabHeaderVisuals visuals, ImageSource source)
    {
        visuals.Favicon.Source = source;
        visuals.Favicon.Visibility = Visibility.Visible;
        visuals.FallbackIcon.Visibility = Visibility.Collapsed;
    }

    private async Task UpdateTabFaviconAsync(BrowserTab tab)
    {
        if (tab.IsClosed || !tab.IsLoaded || tab.View.CoreWebView2 is null)
            return;

        if (tab.Header.Tag is not TabHeaderVisuals visuals)
            return;

        try
        {
            using Stream faviconStream = await tab.View.CoreWebView2.GetFaviconAsync(
                CoreWebView2FaviconImageFormat.Png);

            if (faviconStream.Length == 0)
            {
                ShowFallbackIcon(visuals);
                return;
            }

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = faviconStream;
            bitmap.DecodePixelWidth = 32;
            bitmap.EndInit();
            bitmap.Freeze();

            if (!tab.IsClosed)
                ShowFavicon(visuals, bitmap);
        }
        catch (COMException)
        {
            ShowFallbackIcon(visuals);
        }
        catch (ObjectDisposedException)
        {
            ShowFallbackIcon(visuals);
        }
        catch (InvalidOperationException)
        {
            ShowFallbackIcon(visuals);
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
        CancelSleepSchedule(tab);

        TabStrip.Children.Remove(tab.Header);

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

        if (_tabs.Count == 0)
        {
            await AddTabAsync();
            return;
        }

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

    private void SaveSessionSnapshotIfEnabled()
    {
        if (_sessionTimer.IsEnabled)
            SaveSessionSnapshot();
    }
    private void TabBar_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateTabStripLayout();
    }

    private void UpdateTabStripLayout()
    {
        if (TabBar is null || NewTabButton is null)
            return;

        if (TabStrip.Parent is not ScrollViewer tabScrollViewer)
            return;

        double reservedWidth =
            NewTabButton.ActualWidth +
            NewTabButton.Margin.Left +
            NewTabButton.Margin.Right;

        double horizontalPadding =
            TabBar.Padding.Left +
            TabBar.Padding.Right;

        double availableWidth = Math.Max(
            0,
            TabBar.ActualWidth - horizontalPadding - reservedWidth
        );

        tabScrollViewer.MaxWidth = availableWidth;
    }

}
