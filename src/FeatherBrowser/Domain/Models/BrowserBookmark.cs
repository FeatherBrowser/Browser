namespace FeatherBrowser.Domain.Models;

internal sealed class BrowserBookmark
{
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public string Folder { get; set; } = "Favorites";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
}
