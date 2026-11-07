using FeatherBrowser.Features.Security;

namespace FeatherBrowser.Tests;

public sealed class InternalPageMessagePolicyTests
{
    private const string InternalPage = "https://feather.internal/settings/0123456789abcdef0123456789abcdef";

    [Fact]
    public void TrustedMessageRequiresMatchingTokenAndInternalSources()
    {
        const string token = "abc123";
        string message = """{"action":"open-settings","__featherToken":"abc123"}""";

        Assert.True(InternalPageMessagePolicy.IsTrusted(
            true,
            token,
            InternalPage,
            InternalPage,
            message));
    }

    [Fact]
    public void ExternalDocumentCannotReuseInternalTabState()
    {
        const string token = "abc123";
        string message = """{"action":"open-settings","__featherToken":"abc123"}""";

        Assert.False(InternalPageMessagePolicy.IsTrusted(
            true,
            token,
            "https://example.com/",
            "https://example.com/",
            message));
    }

    [Fact]
    public void MessageWithWrongTokenIsRejected()
    {
        Assert.False(InternalPageMessagePolicy.IsTrusted(
            true,
            "expected",
            InternalPage,
            InternalPage,
            """{"action":"open-settings","__featherToken":"wrong"}"""));
    }

    [Fact]
    public void MalformedMessageIsRejected()
    {
        Assert.False(InternalPageMessagePolicy.IsTrusted(
            true,
            "expected",
            InternalPage,
            InternalPage,
            "{"));
    }

    [Theory]
    [InlineData("about:blank")]
    [InlineData("http://feather.internal/settings/x")]
    [InlineData("https://feather.internal:8443/settings/x")]
    [InlineData("https://user@feather.internal/settings/x")]
    [InlineData("https://feather.internal.example.com/settings/x")]
    [InlineData("https://evil.example/https://feather.internal/settings/x")]
    [InlineData("https://feather.internal/assets/background.png")]
    public void LookalikeOrAssetSourcesAreNotInternalDocuments(string source)
    {
        Assert.False(InternalPageMessagePolicy.IsInternalDocumentSource(source));
        Assert.False(InternalPageMessagePolicy.IsTrusted(
            true,
            "abc123",
            source,
            source,
            """{"action":"open-settings","__featherToken":"abc123"}"""));
    }

    [Fact]
    public void InternalPageCannotBeTrustedFromAnotherFrame()
    {
        Assert.False(InternalPageMessagePolicy.IsTrusted(
            true,
            "abc123",
            InternalPage,
            "https://ads.example/frame",
            """{"action":"open-settings","__featherToken":"abc123"}"""));
    }
}
