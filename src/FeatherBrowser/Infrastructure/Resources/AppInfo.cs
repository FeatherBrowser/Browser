namespace FeatherBrowser.Infrastructure.Resources;

public static class AppInfo
{
    public static string Version => typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "2.0.0";
    public static string DisplayName => $"Feather Browser {Version}";
}
