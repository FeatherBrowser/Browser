using FeatherBrowser.Domain.Models;

namespace FeatherBrowser.Features.Sync.Models;

internal sealed class SyncSnapshot
{
    public int FormatVersion { get; set; } = 1;
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<BrowserBookmark> Bookmarks { get; set; } = [];
    public SyncedBrowserSettings Settings { get; set; } = new();
    public List<HistoryEntry>? History { get; set; }
    public SessionState? Session { get; set; }
}

internal sealed class SyncedBrowserSettings
{
    public List<QuickLink> QuickLinks { get; set; } = [];
    public int BackgroundGraceSeconds { get; set; }
    public bool EcoMode { get; set; }
    public bool MuteBackgroundTabs { get; set; }
    public bool ShieldEnabled { get; set; }
    public bool StrictBlocking { get; set; }
    public bool CosmeticBlocking { get; set; }
    public bool BlockThirdPartyTrackers { get; set; }
    public int SleepAfterSeconds { get; set; }
    public bool LowMemoryMode { get; set; }
    public int MaxLoadedTabs { get; set; }
    public int UnloadAfterSeconds { get; set; }
    public bool KeepAudioTabsLoaded { get; set; }
    public bool StripTrackingParameters { get; set; }
    public string SearchEngine { get; set; } = "Google";
    public bool RestorePreviousSession { get; set; }
    public bool CompactUi { get; set; }
    public bool ShowStatusBar { get; set; }
    public bool AutoMemoryGuard { get; set; }
    public int MemoryGuardMb { get; set; }
    public bool HibernateWhenMinimized { get; set; }
    public bool CrashRecoveryAutosave { get; set; }
    public List<string> KeepAliveSites { get; set; } = [];
    public List<string> AllowlistedSites { get; set; } = [];
    public List<string> CustomBlockRules { get; set; } = [];
    public bool ShowWorkspaceSidebar { get; set; }
    public bool AdaptiveMemoryMode { get; set; }
    public bool ColdUnloadOtherWorkspaces { get; set; }
    public List<string> Workspaces { get; set; } = [];
    public string StartupMode { get; set; } = "Restore";
    public bool BlockNotificationPrompts { get; set; }
    public bool SendDoNotTrack { get; set; }
    public bool SyncHistory { get; set; }
    public bool SyncOpenTabs { get; set; }

    public static SyncedBrowserSettings From(BrowserSettings settings) =>
        new()
        {
            QuickLinks = settings.QuickLinks.Select(link => new QuickLink { Name = link.Name, Url = link.Url }).ToList(),
            BackgroundGraceSeconds = settings.BackgroundGraceSeconds,
            EcoMode = settings.EcoMode,
            MuteBackgroundTabs = settings.MuteBackgroundTabs,
            ShieldEnabled = settings.ShieldEnabled,
            StrictBlocking = settings.StrictBlocking,
            CosmeticBlocking = settings.CosmeticBlocking,
            BlockThirdPartyTrackers = settings.BlockThirdPartyTrackers,
            SleepAfterSeconds = settings.SleepAfterSeconds,
            LowMemoryMode = settings.LowMemoryMode,
            MaxLoadedTabs = settings.MaxLoadedTabs,
            UnloadAfterSeconds = settings.UnloadAfterSeconds,
            KeepAudioTabsLoaded = settings.KeepAudioTabsLoaded,
            StripTrackingParameters = settings.StripTrackingParameters,
            SearchEngine = settings.SearchEngine,
            RestorePreviousSession = settings.RestorePreviousSession,
            CompactUi = settings.CompactUi,
            ShowStatusBar = settings.ShowStatusBar,
            AutoMemoryGuard = settings.AutoMemoryGuard,
            MemoryGuardMb = settings.MemoryGuardMb,
            HibernateWhenMinimized = settings.HibernateWhenMinimized,
            CrashRecoveryAutosave = settings.CrashRecoveryAutosave,
            KeepAliveSites = [.. settings.KeepAliveSites],
            AllowlistedSites = [.. settings.AllowlistedSites],
            CustomBlockRules = [.. settings.CustomBlockRules],
            ShowWorkspaceSidebar = settings.ShowWorkspaceSidebar,
            AdaptiveMemoryMode = settings.AdaptiveMemoryMode,
            ColdUnloadOtherWorkspaces = settings.ColdUnloadOtherWorkspaces,
            Workspaces = [.. settings.Workspaces],
            StartupMode = settings.StartupMode,
            BlockNotificationPrompts = settings.BlockNotificationPrompts,
            SendDoNotTrack = settings.SendDoNotTrack,
            SyncHistory = settings.SyncHistory,
            SyncOpenTabs = settings.SyncOpenTabs
        };

    public void ApplyTo(BrowserSettings settings)
    {
        settings.QuickLinks = QuickLinks.Select(link => new QuickLink { Name = link.Name, Url = link.Url }).ToList();
        settings.BackgroundGraceSeconds = BackgroundGraceSeconds;
        settings.EcoMode = EcoMode;
        settings.MuteBackgroundTabs = MuteBackgroundTabs;
        settings.ShieldEnabled = ShieldEnabled;
        settings.StrictBlocking = StrictBlocking;
        settings.CosmeticBlocking = CosmeticBlocking;
        settings.BlockThirdPartyTrackers = BlockThirdPartyTrackers;
        settings.SleepAfterSeconds = SleepAfterSeconds;
        settings.LowMemoryMode = LowMemoryMode;
        settings.MaxLoadedTabs = MaxLoadedTabs;
        settings.UnloadAfterSeconds = UnloadAfterSeconds;
        settings.KeepAudioTabsLoaded = KeepAudioTabsLoaded;
        settings.StripTrackingParameters = StripTrackingParameters;
        settings.SearchEngine = SearchEngine;
        settings.RestorePreviousSession = RestorePreviousSession;
        settings.CompactUi = CompactUi;
        settings.ShowStatusBar = ShowStatusBar;
        settings.AutoMemoryGuard = AutoMemoryGuard;
        settings.MemoryGuardMb = MemoryGuardMb;
        settings.HibernateWhenMinimized = HibernateWhenMinimized;
        settings.CrashRecoveryAutosave = CrashRecoveryAutosave;
        settings.KeepAliveSites = [.. KeepAliveSites];
        settings.AllowlistedSites = [.. AllowlistedSites];
        settings.CustomBlockRules = [.. CustomBlockRules];
        settings.ShowWorkspaceSidebar = ShowWorkspaceSidebar;
        settings.AdaptiveMemoryMode = AdaptiveMemoryMode;
        settings.ColdUnloadOtherWorkspaces = ColdUnloadOtherWorkspaces;
        settings.Workspaces = [.. Workspaces];
        settings.StartupMode = StartupMode;
        settings.BlockNotificationPrompts = BlockNotificationPrompts;
        settings.SendDoNotTrack = SendDoNotTrack;
        settings.SyncHistory = SyncHistory;
        settings.SyncOpenTabs = SyncOpenTabs;
    }
}
