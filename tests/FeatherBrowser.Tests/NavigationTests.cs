using FeatherBrowser.Features.Navigation;

namespace FeatherBrowser.Tests;

public class NavigationTests
{
    [Theory]
    [InlineData(" example.com ", "Google", "https://example.com/")]
    [InlineData("example.com/path?q=1#part", "Google", "https://example.com/path?q=1#part")]
    [InlineData("http://example.com:8080/path", "Google", "http://example.com:8080/path")]
    [InlineData("file:///C:/test.txt", "Google", "file:///C:/test.txt")]
    [InlineData("two words", "DuckDuckGo", "https://duckduckgo.com/?q=two%20words")]
    [InlineData("a & b", "Bing", "https://www.bing.com/search?q=a%20%26%20b")]
    [InlineData("c# tutorial", "unknown", "https://www.google.com/search?q=c%23%20tutorial")]
    [InlineData("", "Google", "https://www.google.com/search?q=")]
    [InlineData("javascript:alert(1)", "Google", "https://www.google.com/search?q=javascript%3Aalert%281%29")]
    public void NormalizesAddressesAndEscapesSearches(string input, string engine, string expected)
        => Assert.Equal(expected, AddressResolver.Normalize(input, engine));

    [Theory]
    [InlineData("feather://library", "history")]
    [InlineData(" FEATHER://HISTORY ", "history")]
    [InlineData("feather://downloads", "downloads")]
    [InlineData("feather://favorites", "favorites")]
    [InlineData("feather://bookmarks", "favorites")]
    public void ResolvesLibraryAliases(string input, string expected)
    {
        Assert.True(AddressResolver.TryGetLibrarySection(input, out var section));
        Assert.Equal(expected, section);
        Assert.Equal(input.Trim(), AddressResolver.Normalize(input, "Google"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("https://example.com")]
    [InlineData("feather://history.evil.example")]
    [InlineData("feather://downloads/extra")]
    public void RejectsNonLibraryRoutes(string? input)
        => Assert.False(AddressResolver.TryGetLibrarySection(input, out _));

    [Theory]
    [InlineData("feather://settings")]
    [InlineData("FEATHER://WELCOME")]
    public void PreservesInternalPages(string input)
        => Assert.Equal(input, AddressResolver.Normalize(input, "Google"));
}
