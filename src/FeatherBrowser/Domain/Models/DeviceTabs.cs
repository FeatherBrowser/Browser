namespace FeatherBrowser.Domain.Models;

internal sealed class DeviceTabs
{
    public string DeviceId { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public List<SessionTabState> Tabs { get; set; } = [];
}
