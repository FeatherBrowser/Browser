using System.Net;
using System.Text.Json;
using FeatherBrowser.Domain.Models;
using FeatherBrowser.Infrastructure.Resources;

namespace FeatherBrowser.Presentation.Pages;

internal static class SettingsPage
{
    public static string Html(BrowserSettings settings, int bookmarkCount, int historyCount, int passwordCount)
    {
        string json = JsonSerializer.Serialize(settings).Replace("</", "<\\/", StringComparison.Ordinal);
        string allowlist = WebUtility.HtmlEncode(string.Join("\n", settings.AllowlistedSites ?? new List<string>()));
        string customRules = WebUtility.HtmlEncode(string.Join("\n", settings.CustomBlockRules ?? new List<string>()));
        string keepAlive = WebUtility.HtmlEncode(string.Join("\n", settings.KeepAliveSites ?? new List<string>()));

        string template = EmbeddedAssets.LoadPage("settings");

        return template
            .Replace("__SETTINGS_JSON__", json, StringComparison.Ordinal)
            .Replace("__ALLOWLIST__", allowlist, StringComparison.Ordinal)
            .Replace("__CUSTOM_RULES__", customRules, StringComparison.Ordinal)
            .Replace("__KEEP_ALIVE__", keepAlive, StringComparison.Ordinal)
            .Replace("__BOOKMARK_COUNT__", bookmarkCount.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("__HISTORY_COUNT__", historyCount.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("__PASSWORD_COUNT__", passwordCount.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }
}
