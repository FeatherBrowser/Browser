using System.Text;
using System.Text.Json;
using FeatherBrowser.Domain.Models;
using FeatherBrowser.Infrastructure.Resources;
using FeatherBrowser.Presentation.Pages;

namespace FeatherBrowser.Tests;

public class PageRenderingTests
{
    [Theory]
    [InlineData("start")]
    [InlineData("settings")]
    [InlineData("library")]
    [InlineData("welcome")]
    public void PagesComposeEmbeddedStylesAndScripts(string page)
    {
        var html = EmbeddedAssets.LoadPage(page);
        Assert.Contains("<!doctype html>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<style>", html);
        Assert.Contains("<script>", html);
        Assert.DoesNotContain("__PAGE_STYLES__", html);
        Assert.DoesNotContain("__PAGE_SCRIPT__", html);
        Assert.DoesNotContain("__APP_VERSION__", html);
    }

    [Fact]
    public void MissingAssetsHaveActionableErrors()
    {
        var error = Assert.Throws<InvalidOperationException>(() => EmbeddedAssets.Load("missing.js"));
        Assert.Contains("FeatherBrowser.Assets.missing.js", error.Message);
        Assert.Throws<InvalidOperationException>(() => EmbeddedAssets.LoadBytes("missing.js"));
    }

    [Fact]
    public void BinaryAndTextAssetLoadingAgree()
        => Assert.Equal(EmbeddedAssets.Load("autofill.js"),
            Encoding.UTF8.GetString(EmbeddedAssets.LoadBytes("autofill.js")).TrimStart('\uFEFF'));

    [Fact]
    public void HomePagePreventsQuickLinkNamesFromBreakingOutOfScript()
    {
        const string attack = "</script><img src=x onerror=alert(1)>";
        var settings = new BrowserSettings { ActiveWorkspace = attack };
        settings.QuickLinks[0].Name = attack;
        var html = StartPage.Html(attack, attack, settings);
        Assert.DoesNotContain(attack, html);
        Assert.Contains("\\u003C/script\\u003E", html);
        Assert.DoesNotMatch("__[A-Z_]+__", html);
    }

    [Fact]
    public void EmptyQuickLinksStillRenderCompletePage()
    {
        var settings = new BrowserSettings();
        settings.QuickLinks.Clear();
        var html = StartPage.Html("Google", "https://www.google.com/search?q=", settings);
        Assert.DoesNotMatch("__[A-Z_]+__", html);
    }

    [Fact]
    public void UserSettingsRoundTripWithoutLosingCustomizations()
    {
        var settings = new BrowserSettings { ActiveWorkspace = "Work & research", GameMode = true };
        settings.QuickLinks[0].Name = "Quotes \" & <tags>";
        var json = JsonSerializer.Serialize(settings);
        var restored = JsonSerializer.Deserialize<BrowserSettings>(json);
        Assert.NotNull(restored);
        Assert.Equal(settings.ActiveWorkspace, restored.ActiveWorkspace);
        Assert.Equal(settings.QuickLinks[0].Name, restored.QuickLinks[0].Name);
        Assert.True(restored.GameMode);
    }
}
