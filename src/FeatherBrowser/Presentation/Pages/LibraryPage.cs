using System.Text.Json;
using FeatherBrowser.Domain.Models;
using FeatherBrowser.Infrastructure.Resources;

namespace FeatherBrowser.Presentation.Pages;

internal static class LibraryPage
{
    public static string Html(
        IReadOnlyList<BrowserBookmark> bookmarks,
        IReadOnlyList<HistoryEntry> history,
        IReadOnlyList<DownloadEntry> downloads,
        string section)
    {
        string bookmarksJson = JsonSerializer.Serialize(bookmarks.Take(1000)).Replace("</", "<\\/", StringComparison.Ordinal);
        string historyJson = JsonSerializer.Serialize(history.Take(1500)).Replace("</", "<\\/", StringComparison.Ordinal);
        string downloadsJson = JsonSerializer.Serialize(downloads.Take(500)).Replace("</", "<\\/", StringComparison.Ordinal);
        string sectionJson = JsonSerializer.Serialize(string.IsNullOrWhiteSpace(section) ? "history" : section);

        string template = EmbeddedAssets.LoadPage("library");

        return template
            .Replace("__HISTORY_JSON__", historyJson, StringComparison.Ordinal)
            .Replace("__DOWNLOADS_JSON__", downloadsJson, StringComparison.Ordinal)
            .Replace("__BOOKMARKS_JSON__", bookmarksJson, StringComparison.Ordinal)
            .Replace("__SECTION_JSON__", sectionJson, StringComparison.Ordinal);
    }
}
