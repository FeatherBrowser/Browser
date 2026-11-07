using System.Text.Json;
using FeatherBrowser.Domain.Models;
using FeatherBrowser.Features.WebView3;

namespace FeatherBrowser.Tests;

public class PerformancePolicyTests
{
    [Theory]
    [InlineData("balanced", 3, 10, 60, 10, 1200, false, false)]
    [InlineData("saver", 1, 3, 20, 3, 700, false, true)]
    [InlineData("gaming", 1, 1, 8, 0, 600, true, true)]
    [InlineData("responsive", 6, 30, 180, 30, 2000, false, false)]
    public void ProfilesApplyCompletePolicy(string profile, int tabs, int sleep, int unload,
        int grace, int memory, bool gaming, bool cold)
    {
        var settings = new BrowserSettings();
        PerformancePolicy.ApplyProfile(settings, "gaming");
        Assert.True(PerformancePolicy.ApplyProfile(settings, profile));
        Assert.Equal((tabs, sleep, unload, grace, memory), (settings.MaxLoadedTabs,
            settings.SleepAfterSeconds, settings.UnloadAfterSeconds,
            settings.BackgroundGraceSeconds, settings.MemoryGuardMb));
        Assert.Equal(gaming, settings.GameMode);
        Assert.Equal(cold, settings.ColdUnloadOtherWorkspaces);
        Assert.True(settings.EcoMode && settings.LowMemoryMode && settings.AutoMemoryGuard
            && settings.AdaptiveMemoryMode && settings.KeepAudioTabsLoaded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("GAMING")]
    public void UnknownProfileDoesNotMutateSettings(string profile)
    {
        var settings = new BrowserSettings();
        PerformancePolicy.ApplyProfile(settings, "responsive");
        var before = JsonSerializer.Serialize(settings);
        Assert.False(PerformancePolicy.ApplyProfile(settings, profile));
        Assert.Equal(before, JsonSerializer.Serialize(settings));
    }

    [Theory]
    [InlineData(9.999, 10, false, false)]
    [InlineData(10, 10, false, true)]
    [InlineData(10.001, 10, false, true)]
    [InlineData(0, -1, false, true)]
    [InlineData(59, 100, false, false)]
    [InlineData(60, 100, false, true)]
    [InlineData(0, 10, true, true)]
    public void GraceHandlesBoundariesAndGaming(double elapsed, int grace, bool gaming, bool expected)
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(expected, PerformancePolicy.GraceElapsed(now.AddSeconds(-elapsed), now, grace, gaming));
    }
}
