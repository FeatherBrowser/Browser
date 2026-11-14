using System;
using FeatherBrowser.Domain.Models;

namespace FeatherBrowser.Features.WebView3;

internal static class PerformancePolicy
{
    private const int MaxGraceSeconds = 60;
    private const int ActiveSampleIntervalSeconds = 5;
    private const int MinimizedSampleIntervalSeconds = 15;

    public static bool ApplyProfile(BrowserSettings settings, string? profile)
    {
        Profile? selected = profile switch
        {
            "balanced"   => new(3, 10, 60, 10, 1200),
            "saver"      => new(1, 3, 20, 3, 700, ColdUnloadOtherWorkspaces: true),
            "gaming"     => new(1, 1, 8, 0, 600, ColdUnloadOtherWorkspaces: true, Gaming: true),
            "responsive" => new(6, 30, 180, 30, 2000),
            _ => null
        };

        if (selected is not { } preset)
            return false;

        ArgumentNullException.ThrowIfNull(settings);

        settings.EcoMode = true;
        settings.LowMemoryMode = true;
        settings.AutoMemoryGuard = true;
        settings.AdaptiveMemoryMode = true;
        settings.KeepAudioTabsLoaded = true;

        settings.GameMode = preset.Gaming;
        settings.ColdUnloadOtherWorkspaces = preset.ColdUnloadOtherWorkspaces;
        settings.MaxLoadedTabs = preset.MaxLoadedTabs;
        settings.SleepAfterSeconds = preset.SleepAfterSeconds;
        settings.UnloadAfterSeconds = preset.UnloadAfterSeconds;
        settings.BackgroundGraceSeconds = preset.BackgroundGraceSeconds;
        settings.MemoryGuardMb = preset.MemoryGuardMb;

        return true;
    }

    public static bool GraceElapsed(
        DateTime hiddenSince,
        DateTime now,
        int graceSeconds,
        bool gaming) =>
        gaming || now - hiddenSince >= TimeSpan.FromSeconds(
            Math.Clamp(graceSeconds, 0, MaxGraceSeconds));

    public static int SampleIntervalSeconds(bool minimized) =>
        minimized ? MinimizedSampleIntervalSeconds : ActiveSampleIntervalSeconds;

    private readonly record struct Profile(
        int MaxLoadedTabs,
        int SleepAfterSeconds,
        int UnloadAfterSeconds,
        int BackgroundGraceSeconds,
        int MemoryGuardMb,
        bool ColdUnloadOtherWorkspaces = false,
        bool Gaming = false);
}