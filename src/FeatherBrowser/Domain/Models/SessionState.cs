namespace FeatherBrowser.Domain.Models;

internal sealed class SessionState
{
    public List<string> Tabs { get; set; } = [];
    public List<SessionTabState> TabStates { get; set; } = [];
    public int ActiveIndex { get; set; }
    public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.Now;
}
