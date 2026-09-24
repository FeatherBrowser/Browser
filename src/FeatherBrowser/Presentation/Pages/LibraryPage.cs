using System.Text.Json;
using FeatherBrowser.Domain.Models;
using FeatherBrowser.Infrastructure.Resources;

namespace FeatherBrowser.Presentation.Pages;

internal static class LibraryPage
{
    private const int BookmarkLimit = 1_000;
    private const int HistoryLimit = 1_500;
    private const int DownloadLimit = 500;

    private const string DefaultSection = "history";
    private const string TemplateName = "library";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string Html(
        IReadOnlyList<BrowserBookmark> bookmarks,
        IReadOnlyList<HistoryEntry> history,
        IReadOnlyList<DownloadEntry> downloads,
        string? section)
    {
        ArgumentNullException.ThrowIfNull(bookmarks);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(downloads);

        string template = EmbeddedAssets.LoadPage(TemplateName);

        string selectedSection = NormalizeSection(section);

        return template
            .Replace(
                "__BOOKMARKS_JSON__",
                SerializeForHtml(bookmarks.Take(BookmarkLimit)),
                StringComparison.Ordinal)
            .Replace(
                "__HISTORY_JSON__",
                SerializeForHtml(history.Take(HistoryLimit)),
                StringComparison.Ordinal)
            .Replace(
                "__DOWNLOADS_JSON__",
                SerializeForHtml(downloads.Take(DownloadLimit)),
                StringComparison.Ordinal)
            .Replace(
                "__SECTION_JSON__",
                SerializeForHtml(selectedSection),
                StringComparison.Ordinal);
    }

    private static string NormalizeSection(string? section)
    {
        return string.IsNullOrWhiteSpace(section)
            ? DefaultSection
            : section.Trim();
    }

    private static string SerializeForHtml<T>(T value)
    {
        return JsonSerializer
            .Serialize(value, JsonOptions)
            .Replace("</", "<\\/", StringComparison.Ordinal);
    }
}