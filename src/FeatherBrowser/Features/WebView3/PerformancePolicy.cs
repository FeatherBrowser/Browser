using FeatherBrowser.Domain.Models;

namespace FeatherBrowser.Features.WebView3;

internal static class PerformancePolicy
{
    public static bool ApplyProfile(BrowserSettings settings, string profile)
    {
        if (profile is not ("balanced" or "saver" or "gaming" or "responsive")) return false;
        settings.EcoMode = true;
        settings.LowMemoryMode = true;
        settings.AutoMemoryGuard = true;
        settings.AdaptiveMemoryMode = true;
        settings.KeepAudioTabsLoaded = true;
        settings.GameMode = profile == "gaming";
        settings.ColdUnloadOtherWorkspaces = profile is "saver" or "gaming";
        (settings.MaxLoadedTabs, settings.SleepAfterSeconds, settings.UnloadAfterSeconds,
            settings.BackgroundGraceSeconds, settings.MemoryGuardMb) = profile switch
        {
            "saver" => (1, 3, 20, 3, 700),
            "gaming" => (1, 1, 8, 0, 600),
            "responsive" => (6, 30, 180, 30, 2000),
            _ => (3, 10, 60, 10, 1200)
        };
        return true;
    }

    public static bool GraceElapsed(DateTime hiddenSince, DateTime now, int graceSeconds, bool gaming) =>
        gaming || now - hiddenSince >= TimeSpan.FromSeconds(Math.Clamp(graceSeconds, 0, 60));

    public static int SampleIntervalSeconds(bool minimized) => minimized ? 15 : 5;
}
