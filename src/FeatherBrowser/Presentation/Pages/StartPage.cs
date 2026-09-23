using System.Net;
using FeatherBrowser.Domain.Models;
using FeatherBrowser.Infrastructure.Resources;

namespace FeatherBrowser.Presentation.Pages;

internal static class StartPage
{
    public static string Html(
        string searchEngineName,
        string searchPrefix,
        BrowserSettings settings)
    {
        string workspaceText =
            $"{settings.ActiveWorkspace} · " +
            $"{settings.Workspaces.Count} " +
            $"space{(settings.Workspaces.Count == 1 ? "" : "s")}";

        string template = EmbeddedAssets.LoadPage("start");

        return template
            .Replace(
                "__QUICK_LINKS_JSON__",
                System.Text.Json.JsonSerializer.Serialize(
                    settings.QuickLinks ?? []),
                StringComparison.Ordinal)

            .Replace(
                "__HOME_STATE_JSON__",
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    memorySaver =
                        settings.EcoMode ||
                        settings.LowMemoryMode,

                    shield = settings.ShieldEnabled,
                    gaming = settings.GameMode
                }),
                StringComparison.Ordinal)

            .Replace(
                "__SEARCH_ENGINE__",
                WebUtility.HtmlEncode(searchEngineName),
                StringComparison.Ordinal)

            .Replace(
                "__WORKSPACE_TEXT__",
                WebUtility.HtmlEncode(workspaceText),
                StringComparison.Ordinal)

            .Replace(
                "__SEARCH_PREFIX_JSON__",
                System.Text.Json.JsonSerializer.Serialize(searchPrefix),
                StringComparison.Ordinal);
    }
}