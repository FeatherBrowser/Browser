using FeatherBrowser.Features.Security;

namespace FeatherBrowser.Tests;

public sealed class SitePermissionPolicyTests
{
    [Theory]
    [InlineData("https://Example.com/path", "https://example.com")]
    [InlineData("https://example.com:443/path", "https://example.com")]
    [InlineData("http://example.com:80/", "http://example.com")]
    [InlineData("https://example.com:8443/path", "https://example.com:8443")]
    public void NormalizesPermissionOrigin(string value, string expected)
    {
        Assert.True(SitePermissionPolicy.TryNormalizeOrigin(value, out string origin));
        Assert.Equal(expected, origin);
    }

    [Fact]
    public void DifferentPortsProduceDifferentPermissionKeys()
    {
        SitePermissionPolicy.TryNormalizeOrigin("https://example.com/", out string first);
        SitePermissionPolicy.TryNormalizeOrigin("https://example.com:8443/", out string second);

        Assert.NotEqual(
            SitePermissionPolicy.PermissionKey(first, "Camera"),
            SitePermissionPolicy.PermissionKey(second, "Camera"));
    }

    [Fact]
    public void DifferentSchemesProduceDifferentPermissionKeys()
    {
        SitePermissionPolicy.TryNormalizeOrigin("http://example.com/", out string first);
        SitePermissionPolicy.TryNormalizeOrigin("https://example.com/", out string second);

        Assert.NotEqual(
            SitePermissionPolicy.PermissionKey(first, "Geolocation"),
            SitePermissionPolicy.PermissionKey(second, "Geolocation"));
    }

    [Theory]
    [InlineData("file:///C:/test.html")]
    [InlineData("about:blank")]
    [InlineData("not a uri")]
    public void RejectsNonWebOrigins(string value)
    {
        Assert.False(SitePermissionPolicy.TryNormalizeOrigin(value, out _));
    }
}
