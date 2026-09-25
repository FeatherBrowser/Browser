using System.IO;
using FeatherBrowser.Domain.Models;
using FeatherShield;
using FeatherShield.Cosmetic;
using FeatherShield.Filters;
using Microsoft.Web.WebView2.Core;

namespace FeatherBrowser.Features.Blocking;

internal sealed class BlockerEngine
{
    private ShieldEngine _engine = new();

    public BlockerEngine()
    {
        Reload();
    }

    public void Reload(IEnumerable<string>? customRules = null)
    {
        RuleSet rules = LoadBaseRules();

        string legacyRulesPath = Path.Join(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "FeatherBrowser",
            "filters.txt");

        FilterListLoader.LoadRulesFile(
            legacyRulesPath,
            rules);

        if (customRules is not null)
            FilterParser.AddRules(rules, customRules);

        _engine = new ShieldEngine(rules);
    }

    public bool IsSiteAllowlisted(
        string? host,
        BrowserSettings settings)
    {
        return _engine.IsAllowlisted(
            host,
            settings.AllowlistedSites);
    }

    public bool ShouldBlock(
        Uri requestUri,
        string? topLevelAddress,
        CoreWebView2WebResourceContext context,
        BrowserSettings settings)
    {
        Uri? documentUri = null;

        if (Uri.TryCreate(
                topLevelAddress,
                UriKind.Absolute,
                out Uri? parsed))
        {
            documentUri = parsed;
        }

        var request = new ResourceRequest(
            requestUri,
            documentUri,
            MapResourceType(context));

        var options = new ShieldOptions
        {
            Enabled = settings.ShieldEnabled,
            StrictBlocking = settings.StrictBlocking,
            BlockThirdPartyTrackers =
                settings.BlockThirdPartyTrackers,
            AllowlistedSites = settings.AllowlistedSites
        };

        return _engine
            .Evaluate(request, options)
            .IsBlocked;
    }

    public string CleanTopLevelUrl(string address)
    {
        return _engine.CleanTopLevelUrl(address);
    }

    public static string GetCosmeticFilterScript(
        bool strict)
    {
        return CosmeticFilterScripts.Get(strict);
    }

    private static RuleSet LoadBaseRules()
    {
        string filtersRoot = Path.Join(
            AppContext.BaseDirectory,
            "FeatherFilters");

        return Directory.Exists(filtersRoot)
            ? FilterListLoader.LoadDirectory(filtersRoot)
            : new RuleSet();
    }

    private static ResourceType MapResourceType(
        CoreWebView2WebResourceContext context)
    {
        return context.ToString() switch
        {
            "Document" => ResourceType.Document,
            "Script" => ResourceType.Script,
            "Image" => ResourceType.Image,
            "Stylesheet" => ResourceType.Stylesheet,
            "Font" => ResourceType.Font,
            "XmlHttpRequest" => ResourceType.XmlHttpRequest,
            "Fetch" => ResourceType.Fetch,
            "Ping" => ResourceType.Ping,
            "Media" => ResourceType.Media,

            _ => ResourceType.Other
        };
    }
}