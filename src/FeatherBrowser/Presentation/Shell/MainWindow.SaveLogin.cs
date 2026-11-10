using System.Windows;
using FeatherBrowser.Features.Engine;
using FeatherBrowser.Presentation.Tabs;

namespace FeatherBrowser.Presentation.Shell;

public partial class MainWindow
{
    private static readonly TimeSpan PendingLoginLifetime = TimeSpan.FromMinutes(2);

    private LoginSubmission? _pendingLogin;
    private BrowserTab? _pendingLoginTab;
    private DateTime _pendingLoginUtc;

    private void OfferToSaveLogin(BrowserTab tab, LoginSubmission login)
    {
        if (_isPrivateMode || !_store.Settings.OfferToSavePasswords || _passwordVault.LoadError is not null)
            return;

        string host = SafeHost(login.Origin);
        if (_store.Settings.NeverSavePasswordSites.Any(site => string.Equals(site, host, StringComparison.OrdinalIgnoreCase)))
            return;

        try
        {
            if (_passwordVault.IsSaved(login.Origin, login.Username, login.Password))
                return;
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or FormatException)
        {
            return;
        }

        bool update = _passwordVault.HasLoginFor(login.Origin, login.Username);
        _pendingLogin = login;
        _pendingLoginTab = tab;
        _pendingLoginUtc = DateTime.UtcNow;

        SaveLoginText.Text = update ? $"Update the saved password for {host}?" : $"Save password for {host}?";
        SaveLoginDetail.Text = string.IsNullOrEmpty(login.Username) ? "No username" : login.Username;
        SaveLoginButton.Content = update ? "Update" : "Save";
        RefreshSaveLoginBar();
    }

    private void RefreshSaveLoginBar()
    {
        if (_pendingLogin is not null && DateTime.UtcNow - _pendingLoginUtc > PendingLoginLifetime)
            ClearPendingLogin();

        SaveLoginBar.Visibility = _pendingLogin is not null && _pendingLoginTab == _activeTab
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ClearPendingLogin()
    {
        _pendingLogin = null;
        _pendingLoginTab = null;
        SaveLoginBar.Visibility = Visibility.Collapsed;
    }

    private void SaveLoginButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingLogin is not { } login)
            return;

        try
        {
            _passwordVault.Save(login.Origin, login.Username, login.Password);
            StatusText.Text = $"Password saved for {SafeHost(login.Origin)} · fill it with Ctrl+Shift+P";
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or System.IO.IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Feather could not save this password.\n\n{ex.Message}", "Save password",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            ClearPendingLogin();
        }
    }

    private void SaveLoginNeverButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingLogin is { } login)
        {
            string host = SafeHost(login.Origin);
            if (!_store.Settings.NeverSavePasswordSites.Contains(host, StringComparer.OrdinalIgnoreCase))
                _store.Settings.NeverSavePasswordSites.Add(host);
            _store.SaveSettings();
            StatusText.Text = $"Feather won't offer to save passwords for {host}";
        }

        ClearPendingLogin();
    }

    private void SaveLoginDismissButton_Click(object sender, RoutedEventArgs e) => ClearPendingLogin();
}
