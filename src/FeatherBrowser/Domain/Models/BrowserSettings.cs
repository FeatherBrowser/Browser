namespace FeatherBrowser.Domain.Models;

internal sealed class BrowserSettings
{
    public List<QuickLink> QuickLinks { get; set; } =
    [
        new() { Name = "YouTube", Url = "https://www.youtube.com/" },
        new() { Name = "Twitch", Url = "https://www.twitch.tv/" },
        new() { Name = "Reddit", Url = "https://www.reddit.com/" },
        new() { Name = "Spotify", Url = "https://open.spotify.com/" },
        new() { Name = "Notion", Url = "https://www.notion.so/" },
        new() { Name = "GitHub", Url = "https://github.com/" }
    ];
    public int BackgroundGraceSeconds { get; set; } = 10;
    public int SettingsSchemaVersion { get; set; }
    public bool EcoMode { get; set; } = true;
    public bool GameMode { get; set; }
    public bool MuteBackgroundTabs { get; set; } = true;
    public bool ShieldEnabled { get; set; } = true;
    public bool StrictBlocking { get; set; } = true;
    public bool CosmeticBlocking { get; set; } = true;
    public bool BlockThirdPartyTrackers { get; set; } = true;
    public int SleepAfterSeconds { get; set; } = 2;
    public bool LowMemoryMode { get; set; } = true;
    public int MaxLoadedTabs { get; set; } = 1;
    public int UnloadAfterSeconds { get; set; } = 5;
    public bool KeepAudioTabsLoaded { get; set; } = true;
    public bool StripTrackingParameters { get; set; } = true;
    public string SearchEngine { get; set; } = "Google";
    public bool RestorePreviousSession { get; set; } = true;
    public bool CompactUi { get; set; }
    public bool ShowStatusBar { get; set; } = true;
    public bool AutoMemoryGuard { get; set; } = true;
    public int MemoryGuardMb { get; set; } = 700;
    public bool HibernateWhenMinimized { get; set; } = true;
    public bool CrashRecoveryAutosave { get; set; } = true;
    public int LastActiveSessionIndex { get; set; }
    public List<string> KeepAliveSites { get; set; } = [];
    public List<string> AllowlistedSites { get; set; } = [];
    public List<string> CustomBlockRules { get; set; } = [];
    public bool ShowWorkspaceSidebar { get; set; } = true;
    public bool AdaptiveMemoryMode { get; set; } = true;
    public bool ColdUnloadOtherWorkspaces { get; set; } = true;
    public string ActiveWorkspace { get; set; } = "Main";
    public List<string> Workspaces { get; set; } = ["Main", "Gaming", "Work"];
    public string StartupMode { get; set; } = "Restore";
    public bool BlockNotificationPrompts { get; set; } = true;
    public bool SendDoNotTrack { get; set; } = true;
    public bool FirstRunCompleted { get; set; }
    public bool DataBackupsEnabled { get; set; } = true;
    public Dictionary<string, string> SitePermissions { get; set; } = new();
}
