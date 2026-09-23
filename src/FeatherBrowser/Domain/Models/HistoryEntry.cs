namespace FeatherBrowser.Domain.Models;

internal sealed class HistoryEntry
{
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public DateTimeOffset LastVisited { get; set; } = DateTimeOffset.Now;
    public int VisitCount { get; set; } = 1;
}
