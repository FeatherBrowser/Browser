using FeatherBrowser.Infrastructure.Resources;
namespace FeatherBrowser.Presentation.Pages;

internal static class WelcomePage
{
    public static string Html() => EmbeddedAssets.LoadPage("welcome");
}
