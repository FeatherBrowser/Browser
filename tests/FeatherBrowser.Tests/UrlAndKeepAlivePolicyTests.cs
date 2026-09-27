using FeatherBrowser.Features.Navigation;

namespace FeatherBrowser.Tests;

public sealed class UrlIdentityComparerTests
{
    [Fact]
    public void HostAndSchemeCasingDoNotCreateDifferentUrls()
    {
        Assert.True(UrlIdentityComparer.Instance.Equals(
            "HTTPS://Example.COM/Path?Value=One",
            "https://example.com/Path?Value=One"));
    }

    [Fact]
    public void PathCasingRemainsDistinct()
    {
        Assert.False(UrlIdentityComparer.Instance.Equals(
            "https://example.com/File",
            "https://example.com/file"));
    }

    [Fact]
    public void QueryValueCasingRemainsDistinct()
    {
        Assert.False(UrlIdentityComparer.Instance.Equals(
            "https://example.com/?token=ABC",
            "https://example.com/?token=abc"));
    }

    [Fact]
    public void DefaultPortsCompareAsTheSameOrigin()
    {
        Assert.True(UrlIdentityComparer.Instance.Equals(
            "https://example.com:443/a",
            "https://example.com/a"));
    }
}

public sealed class KeepAliveSitePolicyTests
{
    [Fact]
    public void ParentDomainRuleMatchesSubdomain()
    {
        Assert.True(KeepAliveSitePolicy.Matches(
            "media.example.com",
            "example.com"));
    }

    [Fact]
    public void UnrelatedSuffixDoesNotMatch()
    {
        Assert.False(KeepAliveSitePolicy.Matches(
            "notexample.com",
            "example.com"));
    }

    [Fact]
    public void MatchingRulesIncludesInheritedRules()
    {
        List<string> matches = KeepAliveSitePolicy.MatchingRules(
            "media.example.com",
            ["other.test", ".example.com", "media.example.com"]);

        Assert.Equal(2, matches.Count);
    }
}
