using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Controls.Primitives;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using System.Windows;
using FeatherBrowser.Domain.Models;
using FeatherBrowser.Presentation.Tabs;
using FeatherBrowser.Presentation.Pages;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;

namespace FeatherBrowser.Presentation.Shell;

public partial class MainWindow : Window
{
    private void ApplyWelcomePreset(string? preset)
    {
        BrowserSettings settings = _store.Settings;
        settings.EcoMode = true;
        settings.LowMemoryMode = true;
        settings.AutoMemoryGuard = true;
        settings.AdaptiveMemoryMode = true;
        settings.HibernateWhenMinimized = true;
        settings.GameMode = false;

        if (string.Equals(preset, "balanced", StringComparison.OrdinalIgnoreCase))
        {
            settings.MaxLoadedTabs = 3;
            settings.UnloadAfterSeconds = 30;
            settings.SleepAfterSeconds = 5;
            settings.MemoryGuardMb = 1200;
            settings.KeepAudioTabsLoaded = true;
            StatusText.Text = "Balanced profile selected";
        }
        else
        {
            settings.MaxLoadedTabs = 1;
            settings.UnloadAfterSeconds = 3;
            settings.SleepAfterSeconds = 1;
            settings.MemoryGuardMb = 600;
            settings.KeepAudioTabsLoaded = true;
            StatusText.Text = "Low Memory profile selected";
        }

        _ecoMode = settings.EcoMode;
        _store.SaveSettings();
        ApplyRuntimeSettings();
    }

    private void SaveSettingsFromMessage(JsonElement element)
    {
        BrowserSettings settings = _store.Settings;
        settings.SearchEngine = GetString(element, "SearchEngine", settings.SearchEngine) switch
        {
            "Bing" => "Bing",
            "DuckDuckGo" => "DuckDuckGo",
            _ => "Google"
        };
        settings.RestorePreviousSession = GetBool(element, "RestorePreviousSession", settings.RestorePreviousSession);
        settings.CrashRecoveryAutosave = GetBool(element, "CrashRecoveryAutosave", settings.CrashRecoveryAutosave);
        settings.GameMode = GetBool(element, "GameMode", settings.GameMode);
        settings.MuteBackgroundTabs = GetBool(element, "MuteBackgroundTabs", settings.MuteBackgroundTabs);
        settings.EcoMode = GetBool(element, "EcoMode", settings.EcoMode);
        settings.LowMemoryMode = GetBool(element, "LowMemoryMode", settings.LowMemoryMode);
        settings.MaxLoadedTabs = Math.Clamp(GetInt(element, "MaxLoadedTabs", settings.MaxLoadedTabs), 1, 8);
        settings.UnloadAfterSeconds = Math.Clamp(GetInt(element, "UnloadAfterSeconds", settings.UnloadAfterSeconds), 2, 300);
        settings.KeepAudioTabsLoaded = GetBool(element, "KeepAudioTabsLoaded", settings.KeepAudioTabsLoaded);
        settings.HibernateWhenMinimized = GetBool(element, "HibernateWhenMinimized", settings.HibernateWhenMinimized);
        settings.ShieldEnabled = GetBool(element, "ShieldEnabled", settings.ShieldEnabled);
        settings.StrictBlocking = GetBool(element, "StrictBlocking", settings.StrictBlocking);
        settings.BlockThirdPartyTrackers = GetBool(element, "BlockThirdPartyTrackers", settings.BlockThirdPartyTrackers);
        settings.CosmeticBlocking = GetBool(element, "CosmeticBlocking", settings.CosmeticBlocking);
        settings.StripTrackingParameters = GetBool(element, "StripTrackingParameters", settings.StripTrackingParameters);
        settings.CompactUi = GetBool(element, "CompactUi", settings.CompactUi);
        settings.ShowStatusBar = GetBool(element, "ShowStatusBar", settings.ShowStatusBar);
        settings.ShowWorkspaceSidebar = GetBool(element, "ShowWorkspaceSidebar", settings.ShowWorkspaceSidebar);
        settings.AdaptiveMemoryMode = GetBool(element, "AdaptiveMemoryMode", settings.AdaptiveMemoryMode);
        settings.ColdUnloadOtherWorkspaces = GetBool(element, "ColdUnloadOtherWorkspaces", settings.ColdUnloadOtherWorkspaces);
        settings.StartupMode = GetString(element, "StartupMode", settings.StartupMode) switch
        {
            "NewTab" => "NewTab",
            "Gaming" => "Gaming",
            _ => "Restore"
        };
        settings.RestorePreviousSession = settings.StartupMode == "Restore";
        settings.AutoMemoryGuard = GetBool(element, "AutoMemoryGuard", settings.AutoMemoryGuard);
        settings.MemoryGuardMb = Math.Clamp(GetInt(element, "MemoryGuardMb", settings.MemoryGuardMb), 350, 8192);
        settings.BackgroundGraceSeconds = Math.Clamp(GetInt(element, "BackgroundGraceSeconds", settings.BackgroundGraceSeconds), 0, 60);
        settings.SleepAfterSeconds = Math.Clamp(GetInt(element, "SleepAfterSeconds", settings.SleepAfterSeconds), 1, 60);
        settings.KeepAliveSites = SplitRules(GetString(element, "KeepAliveSites", string.Join("\n", settings.KeepAliveSites)));
        settings.AllowlistedSites = SplitRules(GetString(element, "AllowlistedSites", string.Join("\n", settings.AllowlistedSites)));
        settings.CustomBlockRules = SplitRules(GetString(element, "CustomBlockRules", string.Join("\n", settings.CustomBlockRules)));
        settings.BlockNotificationPrompts = GetBool(element, "BlockNotificationPrompts", settings.BlockNotificationPrompts);
        settings.SendDoNotTrack = GetBool(element, "SendDoNotTrack", settings.SendDoNotTrack);
        settings.DataBackupsEnabled = GetBool(element, "DataBackupsEnabled", settings.DataBackupsEnabled);
        settings.AutoSyncEnabled = GetBool(element, "AutoSyncEnabled", settings.AutoSyncEnabled);
        settings.AutoSyncMinutes = Math.Clamp(GetInt(element, "AutoSyncMinutes", settings.AutoSyncMinutes), 1, 60);
        settings.SyncHistory = GetBool(element, "SyncHistory", settings.SyncHistory);
        settings.SyncOpenTabs = GetBool(element, "SyncOpenTabs", settings.SyncOpenTabs);

        if (settings.GameMode)
        {
            settings.EcoMode = true;
            settings.LowMemoryMode = true;
        }

        _store.SaveSettings();
        ApplyRuntimeSettings();
        ConfigureAutomaticSync();
        StatusText.Text = settings.GameMode ? "Settings saved · Gaming Mode active" : "Settings saved";
    }

    private static bool GetBool(JsonElement root, string name, bool fallback) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;

    private static int GetInt(JsonElement root, string name, int fallback) =>
        root.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int result) ? result : fallback;

    private static string GetString(JsonElement root, string name, string fallback) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    private static List<string> SplitRules(string value) => value
        .Split(new[] { '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Take(500)
        .ToList();

    private void ApplyRuntimeSettings()
    {
        _ecoMode = _store.Settings.EcoMode;
        _shieldEnabled = _store.Settings.ShieldEnabled;
        _blocker.Reload(_store.Settings.CustomBlockRules);
        NormalizeWorkspaceSettings();
        ApplyUiPreferences();
        UpdateWorkspaceSidebar();
        ApplyPerformanceToTabs();
        UpdateFeatureButtons();
        UpdateResourceText();
    }

    private void ApplyUiPreferences()
    {
        bool compact = _store.Settings.CompactUi;
        TabBar.Height = compact ? 39 : 43;
        TabBar.Padding = compact ? new Thickness(8, 3, 8, 3) : new Thickness(9, 5, 9, 5);
        Toolbar.Height = compact ? 46 : 54;
        Toolbar.Padding = compact ? new Thickness(8, 5, 8, 5) : new Thickness(9, 8, 9, 8);
        StatusBar.Visibility = _store.Settings.ShowStatusBar && !_isFullscreen ? Visibility.Visible : Visibility.Collapsed;
        WorkspaceSidebar.Visibility = _store.Settings.ShowWorkspaceSidebar && !_isFullscreen ? Visibility.Visible : Visibility.Collapsed;
        AddressBox.FontSize = compact ? 13 : 14;
    }

    private void SetSearchEngine(string engine)
    {
        _store.Settings.SearchEngine = engine;
        _store.SaveSettings();
        StatusText.Text = $"Search engine changed to {engine}";
        if (_activeTab?.IsStartPage == true)
            ShowStartPage(_activeTab);
    }

    private async Task HandleWebMessageAsync(BrowserTab tab, string messageJson)
    {
        if (!tab.IsInternalPage) return;
        try
        {
            using JsonDocument document = JsonDocument.Parse(messageJson);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("action", out JsonElement actionElement)
                || actionElement.ValueKind != JsonValueKind.String)
                return;

            string action = actionElement.GetString() ?? string.Empty;
            switch (action)
            {
                case "apply-webview3-profile":
                    if (tab.IsSettingsPage && FeatherBrowser.Features.WebView3.PerformancePolicy.ApplyProfile(
                        _store.Settings, GetString(root, "profile", "")))
                    {
                        _store.SaveSettings();
                        ApplyRuntimeSettings();
                        ShowSettingsPage(tab);
                        StatusText.Text = "Feather WebView3 performance profile applied";
                    }
                    break;
                case "home-ready":
                    if (tab.IsStartPage) { UpdateResourceText(); SendHomeStats(tab); }
                    break;
                case "home-navigate":
                    if (tab.IsStartPage && root.TryGetProperty("url", out JsonElement homeUrl) && homeUrl.ValueKind == JsonValueKind.String)
                        Navigate(tab, homeUrl.GetString() ?? string.Empty);
                    break;
                case "save-home-links":
                    if (tab.IsStartPage) SaveHomeLinks(root);
                    break;
                case "toggle-memory":
                    if (!tab.IsStartPage) break;
                    bool enabled = !(_store.Settings.EcoMode || _store.Settings.LowMemoryMode);
                    _store.Settings.EcoMode = enabled;
                    _store.Settings.LowMemoryMode = enabled;
                    if (!enabled) _store.Settings.GameMode = false;
                    _store.SaveSettings();
                    ApplyRuntimeSettings();
                    SendHomeStats(tab);
                    break;
                case "toggle-shield":
                    if (!tab.IsStartPage) break;
                    _store.Settings.ShieldEnabled = !_store.Settings.ShieldEnabled;
                    _store.SaveSettings();
                    ApplyRuntimeSettings();
                    SendHomeStats(tab);
                    break;
                case "welcome-preset":
                    if (root.TryGetProperty("preset", out JsonElement presetElement))
                        ApplyWelcomePreset(presetElement.GetString());
                    break;
                case "finish-welcome":
                    _store.Settings.FirstRunCompleted = true;
                    _store.SaveSettings();
                    ShowStartPage(tab);
                    StatusText.Text = $"{FeatherBrowser.Infrastructure.Resources.AppInfo.DisplayName} is ready";
                    break;
                case "open-settings":
                    ShowSettingsPage(tab);
                    break;
                case "open-command-palette":
                    OpenCommandPalette();
                    break;
                case "settings-section-changed":
                    if (tab.IsSettingsPage)
                    {
                        tab.SettingsSection = SettingsPage.NormalizeSection(
                        GetString(root, "section", tab.SettingsSection));
                    }
                break;
                case "toggle-game":
                    ToggleGameMode();
                    if (tab.IsStartPage)
                        SendHomeStats(tab);
                    break;
                case "save-settings":
                    if (root.TryGetProperty("settings", out JsonElement settingsElement))
                        SaveSettingsFromMessage(settingsElement);
                    break;
                case "account-sign-up":
                    if (tab.IsSettingsPage)
                        await SignUpAccountAsync();
                    break;
                case "account-sign-in":
                    if (tab.IsSettingsPage)
                        await SignInAccountAsync();
                    break;
                case "account-sign-out":
                    if (tab.IsSettingsPage)
                        await SignOutAccountAsync();
                    break;
                case "account-setup-mfa":
                    if (tab.IsSettingsPage)
                        await StartTotpEnrollmentAsync();
                    break;
                case "account-verify-mfa":
                    if (tab.IsSettingsPage)
                        await VerifyTotpEnrollmentAsync();
                    break;
                case "account-verify-existing-mfa":
                    if (tab.IsSettingsPage)
                        await VerifyExistingMfaAsync();
                    break;
                case "sync-enable":
                    if (tab.IsSettingsPage)
                        await EnableEncryptedSyncAsync();
                    break;
                case "sync-now":
                    if (tab.IsSettingsPage)
                        await SyncNowAsync();
                    break;
                case "sync-request-approval":
                    if (tab.IsSettingsPage)
                        await RequestDeviceApprovalAsync();
                    break;
                case "sync-check-approval":
                    if (tab.IsSettingsPage)
                        await CheckDeviceApprovalAsync();
                    break;
                case "sync-recover":
                    if (tab.IsSettingsPage)
                        await RecoverEncryptedSyncAsync();
                    break;
                case "sync-recover-file":
                    if (tab.IsSettingsPage)
                        await RecoverEncryptedSyncFromBackupAsync();
                    break;
                case "sync-delete":
                    if (tab.IsSettingsPage)
                        await DeleteEncryptedSyncAsync();
                    break;
                case "device-approve":
                    if (tab.IsSettingsPage)
                        await ApprovePendingDeviceAsync(GetString(root, "deviceId", ""));
                    break;
                case "device-deny":
                    if (tab.IsSettingsPage)
                        await DenyPendingDeviceAsync(GetString(root, "deviceId", ""));
                    break;
                case "import-edge":
                    await ImportFromEdgeAsync();
                    if (tab.IsSettingsPage)
                        ShowSettingsPage(tab);
                    break;
                case "import-passwords":
                    ImportPasswordsFromEdgeCsv();
                    if (tab.IsSettingsPage)
                        ShowSettingsPage(tab);
                    break;
                case "open-edge-password-export":
                    OpenEdgePasswordExport();
                    break;
                case "default-browser":
                    RegisterAsDefaultBrowser();
                    break;
                case "open-downloads":
                    OpenDownloadsFolder();
                    break;
                case "open-download-library":
                    ShowLibraryPage(tab, "downloads");
                    break;
                case "open-history-library":
                    ShowLibraryPage(tab, "history");
                    break;
                case "open-favorites-library":
                    ShowLibraryPage(tab, "favorites");
                    break;
                case "open-url":
                    if (root.TryGetProperty("url", out JsonElement openUrlElement))
                    {
                        string? url = openUrlElement.GetString();
                        if (!string.IsNullOrWhiteSpace(url))
                            Navigate(tab, url);
                    }
                    break;
                case "open-download-file":
                    if (root.TryGetProperty("path", out JsonElement pathElement))
                        OpenDownloadedFile(pathElement.GetString());
                    break;
                case "remove-bookmark":
                    if (root.TryGetProperty("url", out JsonElement bookmarkUrl))
                    {
                        _store.RemoveBookmark(bookmarkUrl.GetString() ?? string.Empty);
                        if (tab.IsLibraryPage)
                            ShowLibraryPage(tab, "favorites");
                    }
                    break;
                case "remove-history":
                    if (root.TryGetProperty("url", out JsonElement historyUrl))
                    {
                        _store.RemoveHistory(historyUrl.GetString() ?? string.Empty);
                        if (tab.IsLibraryPage)
                            ShowLibraryPage(tab, "history");
                    }
                    break;
                case "remove-download":
                    if (root.TryGetProperty("id", out JsonElement downloadId))
                    {
                        _store.RemoveDownload(downloadId.GetString() ?? string.Empty);
                        if (tab.IsLibraryPage)
                            ShowLibraryPage(tab, "downloads");
                    }
                    break;
                case "clear-downloads":
                    ClearDownloadList();
                    if (tab.IsLibraryPage)
                        ShowLibraryPage(tab, "downloads");
                    break;
                case "clear-history":
                    ClearHistory();
                    if (tab.IsSettingsPage)
                        ShowSettingsPage(tab);
                    else if (tab.IsLibraryPage)
                        ShowLibraryPage(tab, "history");
                    break;
                case "reset-site-permissions":
                    _store.Settings.SitePermissions.Clear();
                    _store.SaveSettings();
                    StatusText.Text = "Saved site permission decisions cleared";
                    if (tab.IsSettingsPage)
                        ShowSettingsPage(tab);
                    break;
                case "trim-memory":
                    TrimMemoryNow();
                    if (tab.IsSettingsPage)
                        ShowSettingsPage(tab);
                    break;
                case "performance-center":
                    ShowPerformanceCenter();
                    break;
            }
        }
        catch (JsonException)
        {
        }
    }
}
