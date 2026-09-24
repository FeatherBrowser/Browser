using FeatherBrowser.Features.Navigation;
using FeatherBrowser.Infrastructure.Resources;

int checks = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException(description);
    checks++;
}

Check(AddressResolver.Normalize(" example.com ", "Google") == "https://example.com/", "Bare domain normalization");
Check(AddressResolver.Normalize("https://example.com/path?q=1", "Google") == "https://example.com/path?q=1", "Explicit URL preservation");
Check(AddressResolver.Normalize("two words", "DuckDuckGo") == "https://duckduckgo.com/?q=two%20words", "Search engine selection and encoding");
Check(AddressResolver.GetSearchEngine("unknown").Name == "Google", "Unknown engine fallback");
Check(AddressResolver.Normalize("feather://settings", "Google") == "feather://settings", "Internal settings route");
foreach (string alias in new[] { "feather://library", "feather://history" })
    Check(AddressResolver.TryGetLibrarySection(alias, out string section) && section == "history", $"History alias {alias}");
foreach (string alias in new[] { "feather://favorites", "feather://bookmarks" })
    Check(AddressResolver.TryGetLibrarySection(alias, out string section) && section == "favorites", $"Favorites alias {alias}");
Check(!AddressResolver.TryGetLibrarySection("https://example.com", out _), "External page does not become a library page");
Check(AddressResolver.GetLibraryTitle("downloads") == "Downloads", "Library title");

foreach (string page in new[] { "start", "settings", "library", "welcome" })
{
    string html = EmbeddedAssets.LoadPage(page);
    Check(html.Contains("<!doctype html>", StringComparison.OrdinalIgnoreCase), $"Page loads: {page}");
    Check(!html.Contains("__PAGE_STYLES__") && !html.Contains("__PAGE_SCRIPT__"), $"Styles and scripts composed: {page}");
    Check(html.Contains("<style>") && html.Contains("<script>"), $"Inline assets preserved: {page}");
}
foreach (string script in new[] { "autofill.js", "cosmetic-filter.js", "cosmetic-filter-strict.js" })
    Check(!string.IsNullOrWhiteSpace(EmbeddedAssets.Load(script)), $"Embedded script loads: {script}");
string startTemplate = EmbeddedAssets.LoadPage("start");

foreach (string placeholder in new[]
{
    "__QUICK_LINKS_JSON__",
    "__HOME_STATE_JSON__"
})
{
    Check(
        startTemplate.Contains(placeholder, StringComparison.Ordinal),
        $"Start-page binding placeholder: {placeholder}");
}
Check(EmbeddedAssets.Load("autofill.js").Contains("__PASSWORD_JSON__"), "Autofill binding placeholder");
bool missingAssetRejected = false;
try { EmbeddedAssets.Load("missing.js"); }
catch (InvalidOperationException) { missingAssetRejected = true; }
Check(missingAssetRejected, "Missing assets fail with an explicit error");
var settings = new FeatherBrowser.Domain.Models.BrowserSettings();
Check(settings.QuickLinks.Count == 6, "Six default quick links");
settings.ActiveWorkspace = "<script>alert(1)</script>";
settings.QuickLinks[0].Name = "</script><img src=x onerror=alert(1)>";
string home = FeatherBrowser.Presentation.Pages.StartPage.Html("Google", "https://www.google.com/search?q=", settings);
Check(!System.Text.RegularExpressions.Regex.IsMatch(home, "__[A-Z_]+__"), "All home bindings resolved");
Check(!home.Contains("<script>alert(1)</script>"), "Workspace name HTML encoded");
Check(!home.Contains("</script><img"), "Shortcut JSON safely embedded");
Check(home.Contains("Shared Alpine glass"), "Shared page theme embedded");
string persisted = System.Text.Json.JsonSerializer.Serialize(settings);
var restored = System.Text.Json.JsonSerializer.Deserialize<FeatherBrowser.Domain.Models.BrowserSettings>(persisted)!;
Check(restored.QuickLinks[0].Name == settings.QuickLinks[0].Name, "Quick link settings serialization round-trip");
var policySettings = new FeatherBrowser.Domain.Models.BrowserSettings();
Check(FeatherBrowser.Features.WebView3.PerformancePolicy.ApplyProfile(policySettings, "responsive"), "Responsive profile accepted");
Check(policySettings.MaxLoadedTabs == 6 && policySettings.BackgroundGraceSeconds == 30 && !policySettings.ColdUnloadOtherWorkspaces, "Responsive keeps warm tabs and workspaces");
Check(!FeatherBrowser.Features.WebView3.PerformancePolicy.ApplyProfile(policySettings, "invalid") && policySettings.MaxLoadedTabs == 6, "Invalid profile leaves settings unchanged");
Check(FeatherBrowser.Features.WebView3.PerformancePolicy.ApplyProfile(policySettings, "gaming") && policySettings.GameMode && policySettings.MaxLoadedTabs == 1, "Gaming profile enables one-tab policy");
Check(FeatherBrowser.Features.WebView3.PerformancePolicy.ApplyProfile(policySettings, "balanced") && !policySettings.GameMode && policySettings.KeepAudioTabsLoaded, "Balanced restores audio protection and disables gaming");
var now = DateTime.UtcNow;
Check(!FeatherBrowser.Features.WebView3.PerformancePolicy.GraceElapsed(now.AddSeconds(-2), now, 10, false), "Rapid switches retain grace");
Check(FeatherBrowser.Features.WebView3.PerformancePolicy.GraceElapsed(now.AddSeconds(-10), now, 10, false), "Grace expires at boundary");
Check(FeatherBrowser.Features.WebView3.PerformancePolicy.GraceElapsed(now, now, 10, true), "Gaming bypasses grace");
Check(FeatherBrowser.Features.WebView3.ResourceSampler.CpuPercentage(10, 14, 2, 4) == 50, "CPU normalized across logical processors");
Check(FeatherBrowser.Features.WebView3.ResourceSampler.CpuPercentage(10, 9, 2, 4) is null, "Invalid CPU baseline rejected");
Check(FeatherBrowser.Features.WebView3.ResourceSampler.CpuPercentage(10, 14, 0, 4) is null, "Zero elapsed interval rejected");
var resources = new FeatherBrowser.Features.WebView3.ResourceSnapshot([
    new(1, "UI", 500, 350, 200, 2), new(2, "Renderer", 400, 300, 100, 3)], 2, now);
Check(resources.MemoryBytes == 300 && resources.WorkingSetBytes == 900, "Private RAM separated from summed working sets");
Check(resources.CpuPercent == 5 && !resources.IsPartial, "CPU totals and sample coverage");
var fallback = new FeatherBrowser.Features.WebView3.ResourceSnapshot([
    new(1, "UI", 500, 350, 200, null), new(2, "Renderer", 400, 300, null, null)], 3, now);
Check(fallback.MemoryBytes == 650 && fallback.MemoryLabel == "Private commit" && fallback.IsPartial, "Fallback uses a consistent labelled metric and marks partial coverage");
Check(fallback.CpuPercent is null, "First sample does not invent CPU usage");
Check(FeatherBrowser.Features.WebView3.PerformancePolicy.SampleIntervalSeconds(true) > FeatherBrowser.Features.WebView3.PerformancePolicy.SampleIntervalSeconds(false), "Minimized sampling is less frequent");
Check(EmbeddedAssets.LoadPage("welcome").Contains(AppInfo.Version), "Welcome version supplied by assembly metadata");
Check(EmbeddedAssets.Load("autofill.js").IndexOf("origin-changed", StringComparison.Ordinal) < EmbeddedAssets.Load("autofill.js").IndexOf("const password", StringComparison.Ordinal), "Autofill checks origin before touching credentials");
Console.WriteLine($"Passed {checks} navigation and embedded-resource checks.");
