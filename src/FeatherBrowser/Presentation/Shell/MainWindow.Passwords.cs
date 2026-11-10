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
using FeatherBrowser.Infrastructure.Resources;
using Microsoft.Win32;

namespace FeatherBrowser.Presentation.Shell;

public partial class MainWindow : Window
{
    private async Task AutofillSavedPasswordAsync()
    {
        if (_isPrivateMode)
        {
            StatusText.Text = "Password vault is disabled in Private windows";
            return;
        }
        if (_activeTab is null || _activeTab.IsInternalPage || !_activeTab.IsLoaded)
        {
            StatusText.Text = "Open a website login page first";
            return;
        }

        string source = string.IsNullOrWhiteSpace(_activeTab.View.Source) ? _activeTab.LastAddress : _activeTab.View.Source;
        List<SavedCredential> matches = _passwordVault.FindForUrl(source);
        if (matches.Count == 0)
        {
            StatusText.Text = _passwordVault.LoadError is not null
                ? "Saved passwords could not be read"
                : "No imported password is saved for this site";
            return;
        }

        if (matches.Count == 1)
        {
            await FillCredentialAsync(matches[0]);
            return;
        }

        var chooser = new ContextMenu
        {
            PlacementTarget = MenuButton,
            Placement = PlacementMode.Bottom,
            MinWidth = 260
        };
        chooser.Items.Add(new MenuItem { Header = "Choose account", IsEnabled = false });
        chooser.Items.Add(new Separator());
        foreach (MenuItem item in matches.Select(credential =>
{
    string label = string.IsNullOrWhiteSpace(credential.Username)
        ? "(no username)"
        : credential.Username;
    var menuItem = new MenuItem
    {
        Header = label,
        ToolTip = credential.Origin
    };

    menuItem.Click += async (_, _) => await FillCredentialAsync(credential);

    return menuItem;
}))
        {
            chooser.Items.Add(item);
        }
        chooser.IsOpen = true;
    }

    private async Task FillCredentialAsync(SavedCredential credential)
    {
        if (_isPrivateMode || _activeTab is null || _activeTab.IsInternalPage || !_activeTab.IsLoaded)
            return;

        try
        {
            string current = string.IsNullOrWhiteSpace(_activeTab.View.Source) ? _activeTab.LastAddress : _activeTab.View.Source;
            if (_passwordVault.FindForUrl(current).All(x =>
                    !string.Equals(x.Origin, credential.Origin, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(x.Username, credential.Username, StringComparison.Ordinal)))
            {
                StatusText.Text = "Saved login does not match this site's origin";
                return;
            }

            string password = _passwordVault.RevealPassword(credential);
            string usernameJson = System.Text.Json.JsonSerializer.Serialize(credential.Username);
            string passwordJson = System.Text.Json.JsonSerializer.Serialize(password);
            string scriptTemplate = EmbeddedAssets.Load("autofill.js");
            string script = scriptTemplate
                .Replace("__ORIGIN_JSON__", JsonSerializer.Serialize(new Uri(current).GetLeftPart(UriPartial.Authority)), StringComparison.Ordinal)
                .Replace("__USERNAME_JSON__", usernameJson, StringComparison.Ordinal)
                .Replace("__PASSWORD_JSON__", passwordJson, StringComparison.Ordinal);

            string result = await _activeTab.View.ExecuteScriptAsync(script);
            StatusText.Text = result.Contains("filled", StringComparison.OrdinalIgnoreCase)
                ? $"Filled saved login for {SafeHost(current)}"
                : result.Contains("origin-changed", StringComparison.Ordinal)
                    ? "Page changed; saved login was not filled"
                    : "No password field was found on this page";
        }
        catch (Exception ex) when (FeatherBrowser.Infrastructure.ExceptionFilters.IsRecoverable(ex))
        {
            StatusText.Text = "Could not autofill saved login";
            MessageBox.Show($"Feather could not fill this saved login.\n\n{ex.Message}", "Password autofill", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ClearImportedPasswords()
    {
        if (_passwordVault.LoadError is { } loadError)
        {
            if (MessageBox.Show(
                    $"Feather could not read its saved passwords:\n\n{loadError}\n\nClear the password vault and start over? A backup copy of the unreadable file is kept.",
                    "Forget imported passwords",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            _passwordVault.Clear();
            StatusText.Text = "Imported password vault cleared";
            return;
        }

        if (_passwordVault.Count == 0)
        {
            StatusText.Text = "No imported passwords are stored";
            return;
        }

        if (MessageBox.Show(
                $"Forget all {_passwordVault.Count} passwords imported into Feather's migration vault?\n\nWebsite sign-ins and cookies are not affected.",
                "Forget imported passwords",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        _passwordVault.Clear();
        StatusText.Text = "Imported password vault cleared";
    }
}
