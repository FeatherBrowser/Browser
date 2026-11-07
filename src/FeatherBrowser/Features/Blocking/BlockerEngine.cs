using System.Diagnostics;
using System.IO;
using FeatherBrowser.Domain.Models;
using FeatherBrowser.Features.Engine;
using FeatherShield;
using FeatherShield.Filters;
using FeatherShield.Rules;

namespace FeatherBrowser.Features.Blocking;

internal sealed class BlockerEngine : INetworkPolicy
{
    private static readonly object LoadGate = new();
    private static Task? _loading;
    private static int _generation;
    private static volatile ShieldEngine _shared = new();

    private const int MaxCachedCosmeticScripts = 16;
    private static readonly object CosmeticGate = new();
    private static readonly Dictionary<string, string> _cosmeticScripts = new(StringComparer.OrdinalIgnoreCase);
    private static ShieldEngine? _cosmeticEngine;

    private volatile PolicySnapshot _policy = PolicySnapshot.Disabled;

    private sealed record PolicySnapshot(bool ShieldEnabled, bool StripTracking, bool DoNotTrack, ShieldOptions Options)
    {
        public static readonly PolicySnapshot Disabled = new(false, false, false, new ShieldOptions { Enabled = false });
    }

    public static Task WarmUpAsync(IEnumerable<string>? customRules)
    {
        lock (LoadGate)
        {
            _loading ??= StartBuild(customRules);
            return _loading;
        }
    }

    public static Task ReloadAsync(IEnumerable<string>? customRules)
    {
        lock (LoadGate)
        {
            _loading = StartBuild(customRules);
            return _loading;
        }
    }

    private static Task StartBuild(IEnumerable<string>? customRules)
    {
        string[] rules = customRules?.ToArray() ?? [];
        int generation = ++_generation;
        return Task.Run(() =>
        {
            ShieldEngine engine = Build(rules);
            lock (LoadGate)
            {
                if (generation == _generation)
                    _shared = engine;
            }
        });
    }

    public void ApplySettings(BrowserSettings settings, bool shieldEnabled)
    {
        _policy = new PolicySnapshot(
            shieldEnabled,
            settings.StripTrackingParameters,
            settings.SendDoNotTrack,
            new ShieldOptions
            {
                Enabled = shieldEnabled,
                StrictBlocking = settings.StrictBlocking,
                BlockThirdPartyTrackers = settings.BlockThirdPartyTrackers,
                AllowlistedSites = settings.AllowlistedSites.ToArray()
            });
    }

    public bool SendDoNotTrack => _policy.DoNotTrack;

    public bool IsSiteAllowlisted(string? host, BrowserSettings settings) =>
        _shared.IsAllowlisted(host, settings.AllowlistedSites);

    public bool ShouldBlock(Uri request, string? topLevelAddress, WebResourceKind kind)
    {
        PolicySnapshot policy = _policy;
        if (!policy.ShieldEnabled)
            return false;

        Uri? document = Uri.TryCreate(topLevelAddress, UriKind.Absolute, out Uri? parsed) ? parsed : null;
        return _shared.Evaluate(new ResourceRequest(request, document, Map(kind)), policy.Options).IsBlocked;
    }

    public string? RewriteNavigation(string address)
    {
        if (!_policy.StripTracking)
            return null;

        string cleaned = _shared.CleanTopLevelUrl(address);
        return string.Equals(cleaned, address, StringComparison.Ordinal) ? null : cleaned;
    }

    public string CleanTopLevelUrl(string address) => _shared.CleanTopLevelUrl(address);

    public string GetCosmeticFilterScript(string? pageAddress)
    {
        ShieldEngine engine = _shared;
        string host = Uri.TryCreate(pageAddress, UriKind.Absolute, out Uri? uri) ? uri.Host : string.Empty;

        lock (CosmeticGate)
        {
            if (!ReferenceEquals(engine, _cosmeticEngine) || _cosmeticScripts.Count >= MaxCachedCosmeticScripts)
            {
                _cosmeticScripts.Clear();
                _cosmeticEngine = engine;
            }

            if (!_cosmeticScripts.TryGetValue(host, out string? script))
            {
                script = engine.GetCosmeticFilterScript(pageAddress);
                _cosmeticScripts[host] = script;
            }

            return script;
        }
    }

    private static ShieldEngine Build(string[] customRules)
    {
        var timer = Stopwatch.StartNew();
        string filtersRoot = Path.Join(AppContext.BaseDirectory, "FeatherFilters");
        RuleSet rules = Directory.Exists(filtersRoot) ? FilterListLoader.LoadDirectory(filtersRoot) : new RuleSet();

        string legacyRulesPath = Path.Join(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FeatherBrowser",
            "filters.txt");
        FilterListLoader.LoadRulesFile(legacyRulesPath, rules);

        if (customRules.Length > 0)
            FilterParser.AddRules(rules, customRules);

        Debug.WriteLine(
            $"FeatherShield loaded {rules.NetworkRuleCount} network rules, {rules.ExceptionRuleCount} exceptions, " +
            $"{rules.CosmeticRuleCount} cosmetic rules in {timer.ElapsedMilliseconds} ms.");
        return new ShieldEngine(rules);
    }

    private static ResourceType Map(WebResourceKind kind) => kind switch
    {
        WebResourceKind.Document => ResourceType.Document,
        WebResourceKind.SubDocument => ResourceType.Subdocument,
        WebResourceKind.Script => ResourceType.Script,
        WebResourceKind.Image => ResourceType.Image,
        WebResourceKind.Stylesheet => ResourceType.Stylesheet,
        WebResourceKind.Font => ResourceType.Font,
        WebResourceKind.XmlHttpRequest => ResourceType.XmlHttpRequest,
        WebResourceKind.Ping => ResourceType.Ping,
        WebResourceKind.Media => ResourceType.Media,
        _ => ResourceType.Other
    };
}
