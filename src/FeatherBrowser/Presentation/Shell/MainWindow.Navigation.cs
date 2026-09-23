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
using FeatherBrowser.Features.Navigation;
using FeatherBrowser.Presentation.Tabs;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;

namespace FeatherBrowser.Presentation.Shell;

public partial class MainWindow : Window
{
    private static string DisplayAddress(BrowserTab tab) =>
        tab.IsWelcomePage ? "feather://welcome"
        : tab.IsSettingsPage ? "feather://settings"
        : tab.IsLibraryPage ? $"feather://{tab.LibrarySection}"
        : tab.IsStartPage ? string.Empty
        : tab.LastAddress;

    private void Navigate(BrowserTab tab, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            ShowStartPage(tab);
            return;
        }

        if (value.Equals("feather://newtab", StringComparison.OrdinalIgnoreCase))
        {
            ShowStartPage(tab);
            return;
        }

        if (value.Equals("feather://welcome", StringComparison.OrdinalIgnoreCase))
        {
            ShowWelcomePage(tab);
            return;
        }

        if (value.Equals("feather://settings", StringComparison.OrdinalIgnoreCase))
        {
            ShowSettingsPage(tab);
            return;
        }

        if (TryGetLibrarySection(value, out string librarySection))
        {
            ShowLibraryPage(tab, librarySection);
            return;
        }

        tab.IsStartPage = false;
        tab.IsSettingsPage = false;
        tab.IsLibraryPage = false;
        tab.IsWelcomePage = false;
        string target = NormalizeAddress(value);
        tab.LastAddress = target;
        tab.NeedsContentRestore = true;

        if (tab.IsLoaded)
        {
            tab.View.CoreWebView2.Navigate(target);
            tab.NeedsContentRestore = false;
        }
        else if (_activeTab == tab)
        {
            _ = EnsureTabLoadedAsync(tab);
        }
    }

    private string NormalizeAddress(string input) => AddressResolver.Normalize(input, _store.Settings.SearchEngine);

    private (string Name, string Prefix) SearchEngineInfo() => AddressResolver.GetSearchEngine(_store.Settings.SearchEngine);

    private static string SafeHost(string? address)
    {
        if (Uri.TryCreate(address, UriKind.Absolute, out Uri? uri) && !string.IsNullOrWhiteSpace(uri.Host))
            return uri.Host;
        return "page";
    }

    private static bool TryGetLibrarySection(string? value, out string section) => AddressResolver.TryGetLibrarySection(value, out section);

    private static string LibraryTitle(string section) => AddressResolver.GetLibraryTitle(section);

    private static string TrimLabel(string? value, int max)
    {
        string text = string.IsNullOrWhiteSpace(value) ? "Untitled" : value.Trim();
        return text.Length <= max ? text : text[..(max - 1)] + "…";
    }

    private void NavigateCurrent(string url)
    {
        if (_activeTab is not null)
            Navigate(_activeTab, url);
    }

    private void UpdateNavigationButtons()
    {
        if (_activeTab is null || _activeTab.IsClosed || !_activeTab.IsLoaded)
        {
            BackButton.IsEnabled = false;
            ForwardButton.IsEnabled = false;
            ReloadButton.IsEnabled = _activeTab is not null;
            return;
        }

        CoreWebView2 core = _activeTab.View.CoreWebView2;
        BackButton.IsEnabled = core.CanGoBack;
        ForwardButton.IsEnabled = core.CanGoForward;
        ReloadButton.IsEnabled = true;
    }

    private void UpdateBookmarkButton()
    {
        bool saved = _activeTab is not null && !_activeTab.IsInternalPage && _store.IsBookmarked(_activeTab.LastAddress);
        BookmarkButton.Content = saved ? "★" : "☆";
        BookmarkButton.Foreground = saved ? _accentBrush : (Brush)FindResource("TextPrimary");
    }

    private void UpdateFeatureButtons()
    {
        int blocked = _activeTab?.BlockedRequests ?? 0;
        ShieldButton.Content = _shieldEnabled ? (blocked > 0 ? $"Shield · {blocked}" : "Shield") : "Shield off";
        ShieldButton.Foreground = _shieldEnabled ? _accentBrush : _secondaryBrush;
        GameModeButton.Content = _store.Settings.GameMode ? "Game · ON" : "Game";
        GameModeButton.Foreground = _store.Settings.GameMode ? _gameAccentBrush : _secondaryBrush;
        EcoButton.Content = (_ecoMode || _store.Settings.GameMode) ? "Eco · ON" : "Eco";
        EcoButton.Foreground = (_ecoMode || _store.Settings.GameMode) ? _accentBrush : _secondaryBrush;

        if (_activeTab is null || _activeTab.IsInternalPage || string.IsNullOrWhiteSpace(_activeTab.LastAddress))
        {
            SiteButton.Content = "◉";
            SiteButton.ToolTip = "Site controls";
            SiteButton.Foreground = _secondaryBrush;
        }
        else
        {
            string host = SafeHost(_activeTab.LastAddress);
            bool allowlisted = _blocker.IsSiteAllowlisted(host, _store.Settings);
            SiteButton.Content = allowlisted ? "○" : (_isPrivateMode ? "◐" : "◉");
            SiteButton.ToolTip = allowlisted ? $"Site controls · Shield allowed on {host}" : $"Site controls · {host}";
            SiteButton.Foreground = _isPrivateMode ? new SolidColorBrush(Color.FromRgb(210, 168, 255)) : (allowlisted ? _secondaryBrush : _accentBrush);
        }
    }

    private void AddressBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || _activeTab is null)
            return;

        Navigate(_activeTab, AddressBox.Text);
        Keyboard.ClearFocus();
        e.Handled = true;
    }

    private void AddressBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        AddressBox.Dispatcher.BeginInvoke(new Action(AddressBox.SelectAll));
    }

    private async void NewTabButton_Click(object sender, RoutedEventArgs e) => await AddTabAsync();

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTab is { IsLoaded: true } && _activeTab.View.CoreWebView2.CanGoBack)
            _activeTab.View.CoreWebView2.GoBack();
    }

    private void ForwardButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTab is { IsLoaded: true } && _activeTab.View.CoreWebView2.CanGoForward)
            _activeTab.View.CoreWebView2.GoForward();
    }

    private async void ReloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTab is null)
            return;

        if (!_activeTab.IsLoaded)
        {
            await EnsureTabLoadedAsync(_activeTab);
            return;
        }

        if (_activeTab.IsSettingsPage)
            ShowSettingsPage(_activeTab);
        else if (_activeTab.IsStartPage)
            ShowStartPage(_activeTab);
        else
            _activeTab.View.CoreWebView2.Reload();
    }

    private void HomeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTab is not null)
            ShowStartPage(_activeTab);
    }

    private void BookmarkButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleCurrentBookmark();
    }

    private void ToggleCurrentBookmark()
    {
        if (_activeTab is null || _activeTab.IsInternalPage || string.IsNullOrWhiteSpace(_activeTab.LastAddress))
            return;

        bool added = _store.ToggleBookmark(_activeTab.Title.Text, _activeTab.LastAddress);
        StatusText.Text = added ? "Added to favorites" : "Removed from favorites";
        UpdateBookmarkButton();
    }

    private void EcoButton_Click(object sender, RoutedEventArgs e)
    {
        _ecoMode = !_ecoMode;
        _store.Settings.EcoMode = _ecoMode;
        _store.SaveSettings();
        ApplyPerformanceToTabs();
        UpdateFeatureButtons();
        StatusText.Text = _store.Settings.GameMode
            ? "Gaming Mode controls background tabs while it is enabled"
            : _ecoMode ? "Eco enabled · background tabs suspend quickly" : "Eco suspension disabled";
    }

    private void GameModeButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleGameMode();
        if (_activeTab?.IsStartPage == true)
            ShowStartPage(_activeTab);
    }
}
