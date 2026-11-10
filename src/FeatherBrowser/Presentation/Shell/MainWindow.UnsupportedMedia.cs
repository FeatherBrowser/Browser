using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using FeatherBrowser.Infrastructure.Windows;
using FeatherBrowser.Presentation.Tabs;

namespace FeatherBrowser.Presentation.Shell;

public partial class MainWindow
{
    private BrowserTab? _unsupportedMediaTab;
    private string _unsupportedMediaAddress = "";

    private void OfferOtherBrowserForMedia(BrowserTab tab)
    {
        string address = tab.View.Source;
        if (!Uri.TryCreate(address, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            return;

        _unsupportedMediaTab = tab;
        _unsupportedMediaAddress = address;

        if (_isPrivateMode)
        {
            UnsupportedMediaOpenButton.Visibility = Visibility.Collapsed;
            UnsupportedMediaDetail.Text = "H.264 / AAC · not opened elsewhere from a Private window";
        }
        else
        {
            UnsupportedMediaOpenButton.Visibility = Visibility.Visible;
            UnsupportedMediaOpenButton.Content = DefaultBrowserRegistrar.IsFeatherDefaultForWeb()
                ? "Open in Microsoft Edge"
                : "Open in default browser";
            UnsupportedMediaDetail.Text = "H.264 / AAC";
        }

        RefreshUnsupportedMediaBar();
    }

    private void RefreshUnsupportedMediaBar()
    {
        if (_unsupportedMediaTab is { } tab &&
            (tab.IsClosed || !string.Equals(tab.View.Source, _unsupportedMediaAddress, StringComparison.Ordinal)))
        {
            ClearUnsupportedMedia();
            return;
        }

        UnsupportedMediaBar.Visibility = _unsupportedMediaTab is not null && _unsupportedMediaTab == _activeTab
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ClearUnsupportedMedia()
    {
        _unsupportedMediaTab = null;
        _unsupportedMediaAddress = "";
        UnsupportedMediaBar.Visibility = Visibility.Collapsed;
    }

    private void UnsupportedMediaOpenButton_Click(object sender, RoutedEventArgs e)
    {
        string address = _unsupportedMediaAddress;
        if (_isPrivateMode || address.Length == 0)
            return;

        string target = DefaultBrowserRegistrar.IsFeatherDefaultForWeb() ? "microsoft-edge:" + address : address;
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
            ClearUnsupportedMedia();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            StatusText.Text = "Could not open another browser";
            Trace.TraceWarning($"Opening a page in another browser failed: {ex.Message}");
        }
    }

    private void UnsupportedMediaDismissButton_Click(object sender, RoutedEventArgs e) => ClearUnsupportedMedia();
}
