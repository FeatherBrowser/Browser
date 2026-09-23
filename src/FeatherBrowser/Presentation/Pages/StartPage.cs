using System.Net;
using FeatherBrowser.Domain.Models;
using FeatherBrowser.Infrastructure.Resources;

namespace FeatherBrowser.Presentation.Pages;

internal static class StartPage
{
    public static string Html(string searchEngineName, string searchPrefix, BrowserSettings settings)
    {
        string gameClass = settings.GameMode ? "good" : "muted";
        string gameText = settings.GameMode ? "Gaming mode active" : "Gaming mode off";
        string shieldClass = settings.ShieldEnabled ? "good" : "warn";
        string shieldText = settings.ShieldEnabled ? "Shield enabled" : "Shield disabled";
        string memoryClass = settings.LowMemoryMode ? "good" : "muted";
        string memoryText = settings.LowMemoryMode
            ? $"{settings.MaxLoadedTabs} hot tab{(settings.MaxLoadedTabs == 1 ? "" : "s")} · {settings.UnloadAfterSeconds}s cold unload"
            : settings.EcoMode ? $"Eco suspend · {settings.SleepAfterSeconds}s" : "Memory saver off";
        string gameButton = settings.GameMode ? "Disable gaming mode" : "Enable gaming mode";
        string workspaceText = $"{settings.ActiveWorkspace} · {settings.Workspaces.Count} space{(settings.Workspaces.Count == 1 ? "" : "s")}";

        string template = EmbeddedAssets.LoadPage("start");

        return template
            .Replace("__LANDSCAPE__", EmbeddedAssets.Load("landscape.svg"), StringComparison.Ordinal)
            .Replace("__QUICK_LINKS_JSON__", System.Text.Json.JsonSerializer.Serialize(settings.QuickLinks ?? []), StringComparison.Ordinal)
            .Replace("__HOME_STATE_JSON__", System.Text.Json.JsonSerializer.Serialize(new {
                memorySaver = settings.EcoMode || settings.LowMemoryMode,
                shield = settings.ShieldEnabled, gaming = settings.GameMode
            }), StringComparison.Ordinal)
            .Replace("__SEARCH_ENGINE__", WebUtility.HtmlEncode(searchEngineName), StringComparison.Ordinal)
            .Replace("__GAME_CLASS__", gameClass, StringComparison.Ordinal)
            .Replace("__GAME_TEXT__", WebUtility.HtmlEncode(gameText), StringComparison.Ordinal)
            .Replace("__SHIELD_CLASS__", shieldClass, StringComparison.Ordinal)
            .Replace("__SHIELD_TEXT__", WebUtility.HtmlEncode(shieldText), StringComparison.Ordinal)
            .Replace("__MEMORY_CLASS__", memoryClass, StringComparison.Ordinal)
            .Replace("__MEMORY_TEXT__", WebUtility.HtmlEncode(memoryText), StringComparison.Ordinal)
            .Replace("__GAME_BUTTON__", WebUtility.HtmlEncode(gameButton), StringComparison.Ordinal)
            .Replace("__WORKSPACE_TEXT__", WebUtility.HtmlEncode(workspaceText), StringComparison.Ordinal)
            .Replace("__SEARCH_PREFIX_JSON__", System.Text.Json.JsonSerializer.Serialize(searchPrefix), StringComparison.Ordinal);
    }
}
