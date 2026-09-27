using FeatherBrowser.Features.Security;

namespace FeatherBrowser.Tests;

public sealed class InternalPageMessagePolicyTests
{
    [Fact]
    public void TrustedMessageRequiresMatchingTokenAndInternalSources()
    {
        const string token = "abc123";
        string message = """{"action":"open-settings","__featherToken":"abc123"}""";

        Assert.True(InternalPageMessagePolicy.IsTrusted(
            true,
            token,
            "about:blank",
            "about:blank",
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
            "about:blank",
            "about:blank",
            """{"action":"open-settings","__featherToken":"wrong"}"""));
    }

    [Fact]
    public void MalformedMessageIsRejected()
    {
        Assert.False(InternalPageMessagePolicy.IsTrusted(
            true,
            "expected",
            "about:blank",
            "about:blank",
            "{"));
    }
}
