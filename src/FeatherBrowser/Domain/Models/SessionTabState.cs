namespace FeatherBrowser.Domain.Models;

internal sealed class SessionTabState
{
    public string Address { get; set; } = "feather://newtab";
    public string Title { get; set; } = "New Tab";
    public string Workspace { get; set; } = "Main";
    public bool IsPinned { get; set; }
}
