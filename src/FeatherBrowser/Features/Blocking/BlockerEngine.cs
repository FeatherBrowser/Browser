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

    private const int MaxCachedScriptletScripts = 64;
    private static readonly object ScriptletGate = new();
    private static readonly Dictionary<string, string?> _scriptletScripts = new(StringComparer.OrdinalIgnoreCase);
    private static ShieldEngine? _scriptletEngine;

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

    public bool ShouldBlock(Uri request, string? topLevelAddress, WebResourceKind kind) =>
        Evaluate(request, topLevelAddress, kind).IsBlocked;

    public NetworkVerdict Evaluate(Uri request, string? topLevelAddress, WebResourceKind kind)
    {
        PolicySnapshot policy = _policy;
        if (!policy.ShieldEnabled)
            return NetworkVerdict.Allow;

        Uri? document = Uri.TryCreate(topLevelAddress, UriKind.Absolute, out Uri? parsed) ? parsed : null;
        BlockDecision decision = _shared.Evaluate(new ResourceRequest(request, document, Map(kind)), policy.Options);

        if (!decision.IsBlocked)
            return NetworkVerdict.Allow;

        return RedirectResources.TryGet(decision.Redirect, out RedirectResource stub)
            ? new NetworkVerdict(true, stub.MimeType, stub.Body)
            : NetworkVerdict.Block;
    }

    public string? GetScriptletScript(string? frameAddress, string? topLevelAddress)
    {
        PolicySnapshot policy = _policy;
        if (!policy.ShieldEnabled ||
            !Uri.TryCreate(frameAddress, UriKind.Absolute, out Uri? frame) ||
            frame.Scheme is not ("http" or "https"))
        {
            return null;
        }

        ShieldEngine engine = _shared;
        string topHost = Uri.TryCreate(topLevelAddress, UriKind.Absolute, out Uri? top) ? top.Host : frame.Host;
        if (engine.IsAllowlisted(topHost, policy.Options.AllowlistedSites))
            return null;

        lock (ScriptletGate)
        {
            if (!ReferenceEquals(engine, _scriptletEngine) || _scriptletScripts.Count >= MaxCachedScriptletScripts)
            {
                _scriptletScripts.Clear();
                _scriptletEngine = engine;
            }

            if (!_scriptletScripts.TryGetValue(frame.Host, out string? script))
            {
                script = engine.GetScriptletScript(frameAddress);
                _scriptletScripts[frame.Host] = script;
            }

            return script;
        }
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
            $"{rules.CosmeticRuleCount} cosmetic rules, {rules.RedirectRuleCount} redirects, " +
            $"{rules.ScriptletRuleCount} scriptlets in {timer.ElapsedMilliseconds} ms.");
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
