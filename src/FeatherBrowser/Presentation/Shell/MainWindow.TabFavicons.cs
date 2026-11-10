using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FeatherBrowser.Features.Engine;
using FeatherBrowser.Presentation.Tabs;

namespace FeatherBrowser.Presentation.Shell;

public partial class MainWindow
{
    private sealed class TabHeaderVisuals
    {
        public required Image Favicon { get; init; }
        public required FrameworkElement FallbackIcon { get; init; }
        public required FrameworkElement Spinner { get; init; }
        public required Button CloseButton { get; init; }
        public required TextBlock Title { get; init; }
        public int FaviconRevision { get; set; }
        public string? FaviconHost { get; set; }
        public CancellationTokenSource? FaviconLoad { get; set; }
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

    private static void ResetFaviconIfHostChanged(BrowserTab tab, string url)
    {
        if (tab.Header.Tag is not TabHeaderVisuals visuals)
            return;

        string host = Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ? uri.Host : string.Empty;
        if (string.Equals(visuals.FaviconHost, host, StringComparison.OrdinalIgnoreCase))
            return;

        visuals.FaviconHost = host;
        visuals.FaviconRevision++;
        visuals.FaviconLoad?.Cancel();
        ShowFallbackIcon(visuals);
    }

    private Task UpdateTabFaviconAsync(BrowserTab tab) => UpdateTabFaviconAsync(tab, []);

    private async Task UpdateTabFaviconAsync(BrowserTab tab, IReadOnlyList<string> candidates)
    {
        if (tab.IsClosed || !tab.IsLoaded || tab.Header.Tag is not TabHeaderVisuals visuals)
            return;

        if (tab.IsInternalPage)
        {
            ShowFallbackIcon(visuals);
            return;
        }

        WebPage page = tab.View;
        string pageUrl = page.Source;
        if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out Uri? pageUri) || pageUri.Scheme is not ("http" or "https"))
            return;

        if (candidates.Count == 0 && visuals.Favicon.Source is not null)
            return;

        visuals.FaviconLoad?.Cancel();
        using var load = new CancellationTokenSource();
        visuals.FaviconLoad = load;
        int revision = ++visuals.FaviconRevision;

        bool IsCurrent() =>
            !tab.IsClosed && tab.IsLoaded && ReferenceEquals(tab.View, page) &&
            visuals.FaviconRevision == revision && page.Source == pageUrl;

        try
        {
            byte[]? bytes = await FaviconLoader.LoadAsync(candidates, pageUrl, page.FetchImageAsync, load.Token);
            if (!IsCurrent())
                return;

            if (bytes is null)
            {
                if (_store.GetFavicon(pageUrl) is { Length: > 0 } cached && TryDecodeDataUrl(cached) is { } cachedImage)
                    ShowFavicon(visuals, cachedImage);
                else
                    ShowFallbackIcon(visuals);
                return;
            }

            BitmapSource bitmap = Decode(bytes);
            ShowFavicon(visuals, bitmap);
            visuals.FaviconHost = pageUri.Host;

            if (!_isPrivateMode)
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var image = new MemoryStream();
                encoder.Save(image);
                _store.CacheFavicon(pageUrl, "data:image/png;base64," + Convert.ToBase64String(image.ToArray()));
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException or ArgumentException or FileFormatException)
        {
            if (IsCurrent())
                ShowFallbackIcon(visuals);
        }
        finally
        {
            if (ReferenceEquals(visuals.FaviconLoad, load))
                visuals.FaviconLoad = null;
        }
    }

    private static BitmapSource Decode(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.DecodePixelWidth = 32;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static BitmapSource? TryDecodeDataUrl(string dataUrl)
    {
        int comma = dataUrl.IndexOf(',');
        if (comma < 0)
            return null;

        try
        {
            return Decode(Convert.FromBase64String(dataUrl[(comma + 1)..]));
        }
        catch (Exception ex) when (ex is FormatException or NotSupportedException or IOException or FileFormatException)
        {
            return null;
        }
    }
}
