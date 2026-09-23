using System.IO;
using FeatherBrowser.Domain.Models;
using FeatherBrowser.Infrastructure.Resources;
using Microsoft.Web.WebView2.Core;

namespace FeatherBrowser.Features.Blocking;

internal sealed class BlockerEngine
{
    private readonly HashSet<string> _blockedDomains = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _exceptionDomains = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _urlRules = [];
    private readonly List<string> _exceptionUrlRules = [];

    private static readonly string[] BuiltInDomains =
    [
        "2mdn.net", "33across.com", "adform.net", "adnxs.com", "adroll.com", "adsafeprotected.com",
        "adsrvr.org", "adservice.google.com", "adservice.google.co.uk", "amazon-adsystem.com", "app-measurement.com",
        "atdmt.com", "bidswitch.net", "bluekai.com", "casalemedia.com", "chartbeat.com", "clarity.ms",
        "contextweb.com", "criteo.com", "criteo.net", "demdex.net", "doubleclick.net", "everesttech.net",
        "exelator.com", "flashtalking.com", "googleadservices.com", "googlesyndication.com", "googletagservices.com",
        "google-analytics.com", "analytics.google.com", "stats.g.doubleclick.net", "securepubads.g.doubleclick.net",
        "pagead2.googlesyndication.com", "googletagmanager.com", "gumgum.com", "hotjar.com", "hotjar.io",
        "imrworldwide.com", "indexww.com", "lijit.com", "mathtag.com", "media.net", "moatads.com", "mookie1.com",
        "newrelic.com", "nr-data.net", "omtrdc.net", "openx.net", "outbrain.com", "parsely.com", "pubmatic.com",
        "quantserve.com", "rubiconproject.com", "scorecardresearch.com", "segment.com", "segment.io", "serving-sys.com",
        "sharethrough.com", "smartadserver.com", "spotxchange.com", "taboola.com", "tapad.com", "teads.tv", "turn.com",
        "yieldmo.com", "zedo.com", "ads-twitter.com", "static.ads-twitter.com", "analytics.twitter.com", "ads.linkedin.com",
        "px.ads.linkedin.com", "bat.bing.com", "connect.facebook.net", "analytics.tiktok.com", "business-api.tiktok.com",
        "analytics.snapchat.com", "sc-static.net", "mouseflow.com", "fullstory.com", "optimizely.com", "branch.io",
        "branchlink.com", "kochava.com", "appsflyer.com", "adjust.com", "mixpanel.com", "heap.io", "heapanalytics.com",
        "kissmetrics.io", "crazyegg.com", "quantcount.com", "pixel.quantserve.com", "sentry.io", "browser.sentry-cdn.com",
        "luckyorange.com", "luckyorange.net", "cdn.luckyorange.com", "ads.yahoo.com", "gemini.yahoo.com",
        "advertising.com", "yieldmanager.com", "adcolony.com", "unityads.unity3d.com", "config.unityads.unity3d.com",
        "ads-api.twitter.com", "ads.reddit.com", "events.reddit.com", "alb.reddit.com", "analytics.pinterest.com",
        "ct.pinterest.com", "tr.snapchat.com", "analytics.yahoo.com", "metrika.yandex.ru", "mc.yandex.ru",
        "doubleverify.com", "cdn.doubleverify.com", "googleads.g.doubleclick.net", "ad.doubleclick.net",
        "partner.googleadservices.com", "static.doubleclick.net", "adservice.google.de", "adservice.google.fr",
        "adsystem.amazon.com", "aax.amazon-adsystem.com", "c.amazon-adsystem.com", "fls-na.amazon.com",
        "ads.pubmatic.com", "hbopenbid.pubmatic.com", "ib.adnxs.com", "secure.adnxs.com", "pixel.rubiconproject.com",
        "fastlane.rubiconproject.com", "prebid.a-mo.net", "cdn.taboola.com", "trc.taboola.com", "widgets.outbrain.com",
        "amplify.outbrain.com", "ads.stickyadstv.com", "tracking.adform.net", "track.adform.net", "cm.g.doubleclick.net"
    ];

    private static readonly string[] TrackingTokens =
    [
        "/ads/", "/adserver", "/adservice", "/advert", "/analytics", "/collect", "/beacon", "/pixel", "/telemetry",
        "/tracking", "/tracker", "/pagead", "/prebid", "/sponsor", "doubleclick", "googlesyndication",
        "googleadservices", "ad_unit", "adslot", "ad-slot", "tracking.gif", "pixel.gif", "event.gif", "impression"
    ];

    private static readonly HashSet<string> TrackingQueryParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "fbclid", "gclid", "dclid", "msclkid", "mc_cid", "mc_eid", "igshid", "gbraid", "wbraid", "yclid",
        "_hsenc", "_hsmi", "vero_conv", "vero_id", "oly_anon_id", "oly_enc_id", "wickedid",
        "srsltid", "twclid", "ttclid", "epik", "irclickid", "rb_clickid", "mkt_tok", "trk", "trkCampaign"
    };

    public BlockerEngine() => Reload();

    public void Reload(IEnumerable<string>? customRules = null)
    {
        _blockedDomains.Clear();
        _exceptionDomains.Clear();
        _urlRules.Clear();
        _exceptionUrlRules.Clear();

        foreach (string domain in BuiltInDomains)
            _blockedDomains.Add(domain);

        LoadRulesFromFile();
        if (customRules is not null)
        {
            foreach (string rule in customRules)
                AddRule(rule);
        }
    }

    private void LoadRulesFromFile()
    {
        try
        {
            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FeatherBrowser",
                "filters.txt");

            if (!File.Exists(path))
                return;

            foreach (string raw in File.ReadLines(path))
                AddRule(raw);
        }
        catch
        {
        }
    }

    private void AddRule(string raw)
    {
        string line = (raw ?? string.Empty).Trim();
        if (line.Length == 0 || line.StartsWith('#') || line.StartsWith('!'))
            return;

        bool exception = line.StartsWith("@@", StringComparison.Ordinal);
        if (exception)
            line = line[2..].Trim();

        int optionIndex = line.IndexOf('$');
        if (optionIndex > 0)
            line = line[..optionIndex].Trim();

        if (line.StartsWith("0.0.0.0 ", StringComparison.Ordinal) || line.StartsWith("127.0.0.1 ", StringComparison.Ordinal))
        {
            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length >= 2)
                line = parts[1];
        }

        if (line.StartsWith("||", StringComparison.Ordinal))
            line = line[2..];
        else if (line.StartsWith('|'))
            line = line[1..];
        if (line.EndsWith('^'))
            line = line[..^1];
        if (line.EndsWith('|'))
            line = line[..^1];

        if (Uri.TryCreate(line, UriKind.Absolute, out Uri? uri) && !string.IsNullOrWhiteSpace(uri.Host))
        {
            AddDomainRule(uri.Host.TrimStart('.'), exception);
            return;
        }

        line = line.TrimStart('.');
        if (line.IndexOfAny(new[] { '/', '*', '?', '=' }) >= 0)
        {
            string token = line.Replace("*", "", StringComparison.Ordinal).Trim();
            if (token.Length >= 4)
                (exception ? _exceptionUrlRules : _urlRules).Add(token);
            return;
        }

        if (line.Contains('.') && !line.Contains(' '))
            AddDomainRule(line, exception);
    }

    private void AddDomainRule(string domain, bool exception)
    {
        if (exception)
            _exceptionDomains.Add(domain);
        else
            _blockedDomains.Add(domain);
    }

    public bool IsSiteAllowlisted(string? host, BrowserSettings settings)
    {
        if (string.IsNullOrWhiteSpace(host))
            return false;

        foreach (string raw in settings.AllowlistedSites ?? new List<string>())
        {
            string allowed = NormalizeHost(raw);
            if (allowed.Length == 0)
                continue;
            if (HostMatches(host, allowed))
                return true;
        }

        return false;
    }

    public bool ShouldBlock(Uri requestUri, string? topLevelAddress, CoreWebView2WebResourceContext context, BrowserSettings settings)
    {
        if (!settings.ShieldEnabled ||
            (requestUri.Scheme != Uri.UriSchemeHttp && requestUri.Scheme != Uri.UriSchemeHttps))
            return false;

        string topHost = string.Empty;
        if (Uri.TryCreate(topLevelAddress, UriKind.Absolute, out Uri? topLevelUri))
            topHost = topLevelUri.Host;

        if (IsSiteAllowlisted(topHost, settings) || MatchesException(requestUri))
            return false;

        if (MatchesBlockedDomain(requestUri.Host))
            return true;

        string absolute = requestUri.AbsoluteUri;
        if (_urlRules.Any(rule => absolute.Contains(rule, StringComparison.OrdinalIgnoreCase)))
            return true;

        if (!settings.StrictBlocking)
            return false;

        string resourceKind = context.ToString();
        if (resourceKind.Equals("Document", StringComparison.OrdinalIgnoreCase))
            return false;

        bool thirdParty = topHost.Length > 0 && !HostsRelated(requestUri.Host, topHost);
        if (!thirdParty)
            return false;

        bool trackerLike = LooksLikeTrackingRequest(absolute);
        if (settings.BlockThirdPartyTrackers && trackerLike)
            return true;

        return trackerLike &&
               (resourceKind.Equals("Script", StringComparison.OrdinalIgnoreCase) ||
                resourceKind.Equals("Image", StringComparison.OrdinalIgnoreCase) ||
                resourceKind.Equals("XmlHttpRequest", StringComparison.OrdinalIgnoreCase) ||
                resourceKind.Equals("Fetch", StringComparison.OrdinalIgnoreCase) ||
                resourceKind.Equals("Ping", StringComparison.OrdinalIgnoreCase) ||
                resourceKind.Equals("Media", StringComparison.OrdinalIgnoreCase));
    }

    public string CleanTopLevelUrl(string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrEmpty(uri.Query))
            return address;

        string query = uri.Query.TrimStart('?');
        if (query.Length == 0)
            return address;

        var kept = new List<string>();
        bool changed = false;
        foreach (string part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = part.IndexOf('=');
            string rawName = equals >= 0 ? part[..equals] : part;
            string name;
            try { name = Uri.UnescapeDataString(rawName.Replace('+', ' ')); }
            catch { name = rawName; }

            bool tracking = name.StartsWith("utm_", StringComparison.OrdinalIgnoreCase) || TrackingQueryParameters.Contains(name);
            if (tracking)
                changed = true;
            else
                kept.Add(part);
        }

        if (!changed)
            return address;

        var builder = new UriBuilder(uri) { Query = string.Join("&", kept) };
        return builder.Uri.AbsoluteUri;
    }

    private bool MatchesException(Uri requestUri)
    {
        if (_exceptionDomains.Any(domain => HostMatches(requestUri.Host, domain)))
            return true;
        string absolute = requestUri.AbsoluteUri;
        return _exceptionUrlRules.Any(rule => absolute.Contains(rule, StringComparison.OrdinalIgnoreCase));
    }

    private bool MatchesBlockedDomain(string host) => _blockedDomains.Any(domain => HostMatches(host, domain));

    private static bool HostMatches(string host, string domain) =>
        host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith('.' + domain, StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeTrackingRequest(string url)
    {
        foreach (string token in TrackingTokens)
        {
            if (url.Contains(token, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool HostsRelated(string a, string b) =>
        a.Equals(b, StringComparison.OrdinalIgnoreCase) ||
        a.EndsWith('.' + b, StringComparison.OrdinalIgnoreCase) ||
        b.EndsWith('.' + a, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeHost(string value)
    {
        string raw = (value ?? string.Empty).Trim();
        if (Uri.TryCreate(raw, UriKind.Absolute, out Uri? uri))
            return uri.Host.TrimStart('.');
        return raw.TrimStart('.');
    }

    public static string GetCosmeticFilterScript(bool strict) => strict ? StrictCosmeticFilterScript : CosmeticFilterScript;

    private static string CosmeticFilterScript => EmbeddedAssets.Load("cosmetic-filter.js");

    private static string StrictCosmeticFilterScript => EmbeddedAssets.Load("cosmetic-filter-strict.js");
}
