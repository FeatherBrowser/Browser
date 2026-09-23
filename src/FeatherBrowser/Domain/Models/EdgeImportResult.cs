namespace FeatherBrowser.Domain.Models;

internal sealed class EdgeImportResult
{
    public int ProfilesScanned { get; set; }
    public int BookmarksImported { get; set; }
    public int HistoryImported { get; set; }
    public List<string> SessionUrls { get; } = [];
    public List<string> Warnings { get; } = [];
}
