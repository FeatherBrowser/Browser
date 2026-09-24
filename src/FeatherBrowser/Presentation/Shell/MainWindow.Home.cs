using System.Text.Json;
using System.Windows;
using FeatherBrowser.Domain.Models;
using FeatherBrowser.Presentation.Tabs;

namespace FeatherBrowser.Presentation.Shell;

public partial class MainWindow : Window
{
    private void SendHomeStats(BrowserTab tab)
    {
        if (!tab.IsLoaded || !tab.IsStartPage || tab.IsClosed ||
            !string.Equals(
                tab.View.CoreWebView2.Source,
                "about:blank",
                StringComparison.OrdinalIgnoreCase))
            return;

        var open = _tabs.Where(t => !t.IsClosed).ToList();

        var recent = _store.History
            .Where(entry =>
                !string.IsNullOrWhiteSpace(entry.Url) &&
                (entry.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                 entry.Url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(entry => entry.LastVisited)
            .Take(2)
            .Select(entry =>
            {
                string host = SafeHost(entry.Url);

                return new
                {
                    title = string.IsNullOrWhiteSpace(entry.Title) ? host : entry.Title,
                    url = entry.Url,
                    host
                };
            })
            .ToList();

        tab.View.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            type = "home-stats",
            memory = _lastObservedMemoryBytes > 0
                ? FormatBytes(_lastObservedMemoryBytes)
                : "—",
            memoryLabel = _resourceSnapshot.MemoryLabel +
                (_resourceSnapshot.IsPartial ? " (partial)" : ""),
            cpu = _resourceSnapshot.CpuPercent is double cpu
                ? $"{cpu:0.0}% CPU"
                : "Measuring CPU…",
            ratio = _lastObservedMemoryBytes /
                (Math.Clamp(_store.Settings.MemoryGuardMb, 350, 8192) * 1024d * 1024d),
            loaded = open.Count(t => t.IsLoaded),
            cold = open.Count(t => !t.IsLoaded),
            sleeping = open.Count(t => t.IsLoaded && t.View.CoreWebView2.IsSuspended),
            blocked = open.Sum(t => (long)t.BlockedRequests),
            memorySaver = _store.Settings.EcoMode || _store.Settings.LowMemoryMode,
            shield = _store.Settings.ShieldEnabled,
            gaming = _store.Settings.GameMode,
            favicons = (_store.Settings.QuickLinks ?? [])
                .Select(link => link.Url)
                .Concat(recent.Select(entry => entry.url))
                .Distinct(StringComparer.Ordinal)
                .ToDictionary(url => url, url => _store.GetFavicon(url)),
            recent
        }));
    }

    private void SaveHomeLinks(JsonElement root)
    {
        if (!root.TryGetProperty("links", out JsonElement items) ||
            items.ValueKind != JsonValueKind.Array ||
            items.GetArrayLength() > 12)
            return;

        var links = new List<QuickLink>();

        foreach (JsonElement item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                return;

            string name = GetString(item, "Name", "").Trim();
            string url = GetString(item, "Url", "").Trim();

            if (name.Length is < 1 or > 32 ||
                url.Length > 2048 ||
                !Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                string.IsNullOrWhiteSpace(uri.Host) ||
                !string.IsNullOrEmpty(uri.UserInfo))
                return;

            links.Add(new QuickLink { Name = name, Url = uri.AbsoluteUri });
        }

        _store.Settings.QuickLinks = links;
        _store.SaveSettings();

        string message = JsonSerializer.Serialize(new { type = "home-links", links });

        foreach (BrowserTab home in _tabs.Where(t => !t.IsClosed && t.IsLoaded && t.IsStartPage))
        {
            if (string.Equals(home.View.CoreWebView2.Source, "about:blank", StringComparison.OrdinalIgnoreCase))
                home.View.CoreWebView2.PostWebMessageAsJson(message);
        }
    }
}
