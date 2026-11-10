using FeatherBrowser.Presentation.Tabs;

namespace FeatherBrowser.Presentation.Shell;

public partial class MainWindow
{
    private readonly Dictionary<string, double> _privateSiteZoom = new(StringComparer.OrdinalIgnoreCase);

    private Dictionary<string, double> SiteZoomLevels =>
        _isPrivateMode ? _privateSiteZoom : _store.Settings.SiteZoom ??= new(StringComparer.OrdinalIgnoreCase);

    private static string? ZoomHost(BrowserTab tab) =>
        !tab.IsInternalPage &&
        Uri.TryCreate(tab.LastAddress, UriKind.Absolute, out Uri? uri) &&
        uri.Scheme is "http" or "https"
            ? uri.Host
            : null;

    private void RememberSiteZoom(BrowserTab tab, double factor)
    {
        if (ZoomHost(tab) is not { } host)
            return;

        if (Math.Abs(factor - 1.0) < 0.001)
            SiteZoomLevels.Remove(host);
        else
            SiteZoomLevels[host] = factor;

        if (!_isPrivateMode)
            _store.SaveSettings();

        foreach (BrowserTab other in _tabs.Where(t => t != tab && t.IsLoaded && !t.IsClosed))
        {
            if (string.Equals(ZoomHost(other), host, StringComparison.OrdinalIgnoreCase))
                other.View.ZoomFactor = factor;
        }
    }

    private async Task TogglePictureInPictureAsync()
    {
        if (_activeTab is not { IsLoaded: true, IsInternalPage: false } tab)
            return;

        StatusText.Text = await tab.View.TogglePictureInPictureAsync() switch
        {
            "entered" => "Video opened in picture-in-picture",
            "exited" => "Picture-in-picture closed",
            "no-video" => "No video on this page can play in picture-in-picture",
            _ => "This page doesn't allow picture-in-picture"
        };
    }

    private async Task ToggleReaderViewAsync()
    {
        if (_activeTab is not { IsLoaded: true, IsInternalPage: false } tab)
            return;

        if (tab.IsReaderView)
        {
            tab.IsReaderView = false;
            tab.View.Reload();
            return;
        }

        string result = await tab.View.ExecuteScriptAsync(FeatherBrowser.Infrastructure.Resources.EmbeddedAssets.Load("reader.js"));
        if (result.Contains("\"ok\"", StringComparison.Ordinal))
        {
            tab.IsReaderView = true;
            StatusText.Text = "Reader view · open it again from the menu to return to the full page";
        }
        else
        {
            StatusText.Text = result.Contains("no-article", StringComparison.Ordinal)
                ? "Reader view couldn't find an article on this page"
                : "Reader view isn't available on this page";
        }
    }

    private void ApplySiteZoom(BrowserTab tab)
    {
        if (!tab.IsLoaded || ZoomHost(tab) is not { } host)
            return;

        double factor = SiteZoomLevels.TryGetValue(host, out double saved) ? saved : 1.0;
        if (Math.Abs(tab.View.ZoomFactor - factor) > 0.001)
            tab.View.ZoomFactor = factor;
    }
}
