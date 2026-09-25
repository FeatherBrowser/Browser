using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FeatherBrowser.Presentation.Tabs;
using Microsoft.Web.WebView2.Core;

namespace FeatherBrowser.Presentation.Shell;

public partial class MainWindow
{
    private sealed class TabHeaderVisuals
    {
        public required Image Favicon { get; init; }
        public required FrameworkElement FallbackIcon { get; init; }
        public CoreWebView2? FaviconCore { get; set; }
        public int FaviconRevision { get; set; }
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

        core.NavigationStarting += (_, _) =>
        {
            visuals.FaviconRevision++;
            ShowFallbackIcon(visuals);
        };

        core.FaviconChanged += async (_, _) =>
            await UpdateTabFaviconAsync(tab);

        core.NavigationCompleted += async (_, _) =>
            await UpdateTabFaviconAsync(tab);
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

        CoreWebView2 core = tab.View.CoreWebView2;
        string pageUrl = core.Source;
        int revision = ++visuals.FaviconRevision;

        bool IsCurrent() =>
            !tab.IsClosed &&
            tab.IsLoaded &&
            ReferenceEquals(tab.View.CoreWebView2, core) &&
            visuals.FaviconRevision == revision;

        try
        {
            using Stream faviconStream = await core.GetFaviconAsync(
                CoreWebView2FaviconImageFormat.Png);

            if (!IsCurrent() || core.Source != pageUrl)
                return;

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

            ShowFavicon(visuals, bitmap);

            if (!_isPrivateMode && !tab.IsInternalPage)
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));

                using var image = new MemoryStream();
                encoder.Save(image);

                _store.CacheFavicon(
                    pageUrl,
                    "data:image/png;base64," +
                    Convert.ToBase64String(image.ToArray()));
            }
        }
        catch (Exception ex) when (ex is COMException or ObjectDisposedException or
                                   InvalidOperationException or IOException or NotSupportedException)
        {
            if (IsCurrent())
                ShowFallbackIcon(visuals);
        }
    }
}
