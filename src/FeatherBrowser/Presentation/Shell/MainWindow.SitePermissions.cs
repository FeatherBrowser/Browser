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
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;

namespace FeatherBrowser.Presentation.Shell;

public partial class MainWindow : Window
{
    private static string PermissionKey(string host, string kind) => $"{host.ToLowerInvariant()}|{kind}";

    private string GetSitePermission(string host, string kind)
    {
        string key = PermissionKey(host, kind);
        return _store.Settings.SitePermissions.TryGetValue(key, out string? value) ? value : "Ask";
    }

    private void SetSitePermission(string host, string kind, string value)
    {
        string key = PermissionKey(host, kind);
        if (string.Equals(value, "Ask", StringComparison.OrdinalIgnoreCase))
            _store.Settings.SitePermissions.Remove(key);
        else
            _store.Settings.SitePermissions[key] = value;
        _store.SaveSettings();
        StatusText.Text = $"{kind} permission for {host}: {value}";
    }

    private void ShieldButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = ShieldButton,
            Placement = PlacementMode.Bottom,
            MinWidth = 270
        };

        var enabled = new MenuItem { Header = "Feather Shield", IsCheckable = true, IsChecked = _store.Settings.ShieldEnabled };
        enabled.Click += (_, _) =>
        {
            _store.Settings.ShieldEnabled = !_store.Settings.ShieldEnabled;
            _shieldEnabled = _store.Settings.ShieldEnabled;
            _store.SaveSettings();
            UpdateFeatureButtons();
            if (_activeTab is { IsInternalPage: false, IsLoaded: true })
                _activeTab.View.CoreWebView2.Reload();
        };
        menu.Items.Add(enabled);

        var strict = new MenuItem { Header = "Strict blocking", IsCheckable = true, IsChecked = _store.Settings.StrictBlocking };
        strict.Click += (_, _) =>
        {
            _store.Settings.StrictBlocking = !_store.Settings.StrictBlocking;
            _store.SaveSettings();
            if (_activeTab is { IsInternalPage: false, IsLoaded: true })
                _activeTab.View.CoreWebView2.Reload();
        };
        menu.Items.Add(strict);

        string currentHost = _activeTab is null ? string.Empty : SafeHost(_activeTab.LastAddress);
        if (!string.IsNullOrWhiteSpace(currentHost) && currentHost != "page" && _activeTab?.IsInternalPage == false)
        {
            bool allowed = _blocker.IsSiteAllowlisted(currentHost, _store.Settings);
            var site = new MenuItem
            {
                Header = allowed ? $"Enable Shield on {currentHost}" : $"Allow ads on {currentHost}"
            };
            site.Click += (_, _) =>
            {
                if (allowed)
                    _store.Settings.AllowlistedSites.RemoveAll(x =>
                    {
                        string value = x.Trim().TrimStart('.');
                        return currentHost.Equals(value, StringComparison.OrdinalIgnoreCase) ||
                               currentHost.EndsWith('.' + value, StringComparison.OrdinalIgnoreCase);
                    });
                else if (!_store.Settings.AllowlistedSites.Contains(currentHost, StringComparer.OrdinalIgnoreCase))
                    _store.Settings.AllowlistedSites.Add(currentHost);
                _store.SaveSettings();
                if (_activeTab is { IsLoaded: true })
                    _activeTab.View.CoreWebView2.Reload();
            };
            menu.Items.Add(site);
        }

        menu.Items.Add(new Separator());
        var settings = new MenuItem { Header = $"Open Shield settings · {_activeTab?.BlockedRequests ?? 0} blocked on this tab" };
        settings.Click += async (_, _) => await OpenSettingsAsync();
        menu.Items.Add(settings);
        menu.IsOpen = true;
    }

    private void SiteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTab is null || _activeTab.IsInternalPage || string.IsNullOrWhiteSpace(_activeTab.LastAddress))
        {
            StatusText.Text = "Site controls are available on normal web pages";
            return;
        }

        string host = SafeHost(_activeTab.LastAddress);
        if (string.IsNullOrWhiteSpace(host) || host == "page")
            return;

        var menu = new ContextMenu
        {
            PlacementTarget = SiteButton,
            Placement = PlacementMode.Bottom,
            MinWidth = 290
        };

        var title = new MenuItem { Header = host, IsEnabled = false, FontWeight = FontWeights.SemiBold };
        menu.Items.Add(title);
        if (_isPrivateMode)
            menu.Items.Add(new MenuItem { Header = "Private profile · data is temporary", IsEnabled = false });
        menu.Items.Add(new Separator());

        bool allowlisted = _blocker.IsSiteAllowlisted(host, _store.Settings);
        var shield = new MenuItem { Header = "Feather Shield on this site", IsCheckable = true, IsChecked = !allowlisted };
        shield.Click += (_, _) =>
        {
            if (allowlisted)
                _store.Settings.AllowlistedSites.RemoveAll(x => string.Equals(x, host, StringComparison.OrdinalIgnoreCase));
            else if (!_store.Settings.AllowlistedSites.Contains(host, StringComparer.OrdinalIgnoreCase))
                _store.Settings.AllowlistedSites.Add(host);
            _store.SaveSettings();
            StatusText.Text = allowlisted ? $"Shield enabled for {host}" : $"Shield disabled for {host}";
            if (_activeTab is { IsLoaded: true })
                _activeTab.View.CoreWebView2.Reload();
        };
        menu.Items.Add(shield);

        bool keepAlive = _store.Settings.KeepAliveSites.Contains(host, StringComparer.OrdinalIgnoreCase);
        var keepAliveItem = new MenuItem { Header = "Keep site alive in background", IsCheckable = true, IsChecked = keepAlive };
        keepAliveItem.Click += (_, _) =>
        {
            if (keepAlive)
                _store.Settings.KeepAliveSites.RemoveAll(x => string.Equals(x, host, StringComparison.OrdinalIgnoreCase));
            else
                _store.Settings.KeepAliveSites.Add(host);
            _store.SaveSettings();
            StatusText.Text = keepAlive ? $"{host} can now cold-unload" : $"{host} will stay alive in background";
        };
        menu.Items.Add(keepAliveItem);

        var permissions = new MenuItem { Header = "Permissions" };
        foreach (string kind in new[] { "Camera", "Microphone", "Geolocation", "Notifications" })
        {
            var kindMenu = new MenuItem { Header = kind };
            string current = GetSitePermission(host, kind);
            foreach (string choice in new[] { "Ask", "Allow", "Block" })
            {
                var choiceItem = new MenuItem { Header = choice, IsCheckable = true, IsChecked = string.Equals(current, choice, StringComparison.OrdinalIgnoreCase) };
                string capturedKind = kind;
                string capturedChoice = choice;
                choiceItem.Click += (_, _) => SetSitePermission(host, capturedKind, capturedChoice);
                kindMenu.Items.Add(choiceItem);
            }
            permissions.Items.Add(kindMenu);
        }
        menu.Items.Add(permissions);

        menu.Items.Add(new Separator());
        var clearCookies = new MenuItem { Header = "Clear cookies for this site" };
        clearCookies.Click += async (_, _) => await ClearCurrentSiteCookiesAsync();
        menu.Items.Add(clearCookies);

        var copyUrl = new MenuItem { Header = "Copy page address" };
        copyUrl.Click += (_, _) =>
        {
            try { Clipboard.SetText(_activeTab?.LastAddress ?? string.Empty); }
            catch { }
        };
        menu.Items.Add(copyUrl);
        menu.IsOpen = true;
    }

    private async Task ClearCurrentSiteCookiesAsync()
    {
        if (_activeTab is not { IsLoaded: true } tab || tab.IsInternalPage || string.IsNullOrWhiteSpace(tab.LastAddress))
            return;

        try
        {
            var cookies = await tab.View.CoreWebView2.CookieManager.GetCookiesAsync(tab.LastAddress);
            foreach (CoreWebView2Cookie cookie in cookies)
                tab.View.CoreWebView2.CookieManager.DeleteCookie(cookie);
            StatusText.Text = $"Cleared {cookies.Count} cookie{(cookies.Count == 1 ? "" : "s")} for {SafeHost(tab.LastAddress)}";
            tab.View.Reload();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not clear site cookies: {ex.Message}";
        }
    }
}
