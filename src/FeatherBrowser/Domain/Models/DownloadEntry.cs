namespace FeatherBrowser.Domain.Models;

internal sealed class DownloadEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FileName { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public string State { get; set; } = "In progress";
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? CompletedAt { get; set; }
}
