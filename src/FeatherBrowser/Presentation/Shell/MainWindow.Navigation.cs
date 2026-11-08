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

        ClearInternalPageState(tab);

        string target = NormalizeAddress(value);
        tab.LastAddress = target;
        tab.NeedsContentRestore = true;

        if (tab.IsLoaded)
        {
            tab.View.Navigate(target);
            tab.NeedsContentRestore = false;
        }
        else if (_activeTab == tab)
        {
            _ = EnsureTabLoadedAsync(tab);
        }
    }

    private void AddressBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        AddressPlaceholder.Visibility =
            string.IsNullOrWhiteSpace(AddressBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
        UpdateSiteIndicator();
    }

    private void AddressBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => UpdateSiteIndicator();

    private void UpdateSiteIndicator()
    {
        if (AddressSearchIcon is null)
            return;

        string kind = "search";
        string tip = "Search or enter address";
        if (!AddressBox.IsKeyboardFocusWithin && _activeTab is { } tab)
        {
            if (tab.IsInternalPage && !tab.IsStartPage)
            {
                kind = "feather";
                tip = "Feather page";
            }
            else if (Uri.TryCreate(tab.LastAddress, UriKind.Absolute, out Uri? uri))
            {
                (kind, tip) = uri.Scheme switch
                {
                    "https" => ("lock", $"Connection to {uri.Host} is encrypted · site settings"),
                    "http" => ("warning", $"Not secure · information sent to {uri.Host} is not encrypted"),
                    _ => ("search", "Site information")
                };
            }
        }

        AddressSearchIcon.Visibility = kind == "search" ? Visibility.Visible : Visibility.Collapsed;
        AddressLockIcon.Visibility = kind == "lock" ? Visibility.Visible : Visibility.Collapsed;
        AddressWarningIcon.Visibility = kind == "warning" ? Visibility.Visible : Visibility.Collapsed;
        AddressFeatherIcon.Visibility = kind == "feather" ? Visibility.Visible : Visibility.Collapsed;
        SiteButton.ToolTip = tip;
    }

    private string NormalizeAddress(string input) =>
        AddressResolver.Normalize(input, _store.Settings.SearchEngine);

    private (string Name, string Prefix) SearchEngineInfo() =>
        AddressResolver.GetSearchEngine(_store.Settings.SearchEngine);

    private static string SafeHost(string? address)
    {
        if (Uri.TryCreate(address, UriKind.Absolute, out Uri? uri) &&
            !string.IsNullOrWhiteSpace(uri.Host))
        {
            return uri.Host;
        }

        return "page";
    }

    private static bool TryGetLibrarySection(string? value, out string section) =>
        AddressResolver.TryGetLibrarySection(value, out section);

    private static string LibraryTitle(string section) =>
        AddressResolver.GetLibraryTitle(section);

    private static string TrimLabel(string? value, int max)
    {
        string text = string.IsNullOrWhiteSpace(value)
            ? "Untitled"
            : value.Trim();

        return text.Length <= max
            ? text
            : text[..(max - 1)] + "…";
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
            ForwardNavButton.IsEnabled = false;
            ReloadButton.IsEnabled = _activeTab is not null;
            return;
        }

        BackButton.IsEnabled = _activeTab.View.CanGoBack;
        ForwardNavButton.IsEnabled = _activeTab.View.CanGoForward;
        ReloadButton.IsEnabled = true;
    }

    private void UpdateBookmarkButton()
    {
        bool saved =
            _activeTab is not null &&
            !_activeTab.IsInternalPage &&
            _store.IsBookmarked(_activeTab.LastAddress);

        BookmarkIcon.Text = saved ? "" : "";
        if (saved)
            BookmarkIcon.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        else
            BookmarkIcon.ClearValue(TextBlock.ForegroundProperty);

        BookmarkButton.ToolTip = saved
            ? "Remove from favorites (Ctrl+D)"
            : "Add to favorites (Ctrl+D)";
    }

    private void UpdateFeatureButtons()
    {
        int blocked = _activeTab?.BlockedRequests ?? 0;

        ShieldButton.Foreground =
            _shieldEnabled
                ? _accentBrush
                : _secondaryBrush;

        ShieldButton.ToolTip =
            _shieldEnabled
                ? blocked > 0
                    ? $"Feather Shield · {blocked} requests blocked"
                    : "Feather Shield enabled"
                : "Feather Shield disabled";
    }

    private void AddressBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || _activeTab is null)
            return;

        Navigate(_activeTab, AddressBox.Text);
        Keyboard.ClearFocus();
        if (_activeTab.IsLoaded)
            _activeTab.View.Focus();
        UpdateSiteIndicator();
        e.Handled = true;
    }

    private void AddressBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        AddressBox.Dispatcher.BeginInvoke(new Action(AddressBox.SelectAll));
    }

    private async void NewTabButton_Click(object sender, RoutedEventArgs e) =>
        await AddTabAsync();

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTab is { IsLoaded: true })
            _activeTab.View.GoBack();
    }

    private void ForwardButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTab is { IsLoaded: true })
            _activeTab.View.GoForward();
    }

    private async void ReloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTab is null)
            return;

        if (!_activeTab.IsLoaded)
        {
            BrowserTab tab = _activeTab;
            if (!await TryEnsureTabLoadedAsync(tab) || tab.IsClosed || _isClosing || _activeTab != tab)
                return;

            ShowActiveTab(tab);
            RefreshActiveTabUi(tab);
            return;
        }

        if (_activeTab.IsSettingsPage)
            ShowSettingsPage(_activeTab);
        else if (_activeTab.IsStartPage)
            ShowStartPage(_activeTab);
        else if (_activeTab.IsLibraryPage)
            ShowLibraryPage(_activeTab, _activeTab.LibrarySection);
        else if (_activeTab.IsWelcomePage)
            ShowWelcomePage(_activeTab);
        else
            _activeTab.View.Reload(ignoreCache: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
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
        if (_activeTab is null ||
            _activeTab.IsInternalPage ||
            string.IsNullOrWhiteSpace(_activeTab.LastAddress))
        {
            return;
        }

        bool added = _store.ToggleBookmark(
            _activeTab.Title.Text,
            _activeTab.LastAddress);

        StatusText.Text = !_store.LastSaveSucceeded
            ? "Favorites changed, but could not be saved to disk"
            : added
                ? "Added to favorites"
                : "Removed from favorites";

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
            : _ecoMode
                ? "Eco enabled · background tabs suspend quickly"
                : "Eco suspension disabled";
    }

    private void GameModeButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleGameMode();

        if (_activeTab?.IsStartPage == true)
            ShowStartPage(_activeTab);
    }
}
