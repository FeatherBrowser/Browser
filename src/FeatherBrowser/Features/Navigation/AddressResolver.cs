namespace FeatherBrowser.Features.Navigation;

internal static class AddressResolver
{
    public static string Normalize(string input, string searchEngine)
    {
        string value = input.Trim();

        if (value.Equals("feather://welcome", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("feather://settings", StringComparison.OrdinalIgnoreCase) ||
            TryGetLibrarySection(value, out _))
            return value;

        if (Uri.TryCreate(value, UriKind.Absolute, out Uri? absolute) &&
            (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps || absolute.Scheme == Uri.UriSchemeFile))
            return absolute.ToString();

        if (!value.Contains(' ') && value.Contains('.'))
        {
            if (Uri.TryCreate("https://" + value, UriKind.Absolute, out Uri? guessed))
                return guessed.ToString();
        }

        return GetSearchEngine(searchEngine).Prefix + Uri.EscapeDataString(value);
    }

    public static (string Name, string Prefix) GetSearchEngine(string searchEngine) => searchEngine switch
    {
        "Bing" => ("Bing", "https://www.bing.com/search?q="),
        "DuckDuckGo" => ("DuckDuckGo", "https://duckduckgo.com/?q="),
        _ => ("Google", "https://www.google.com/search?q=")
    };

    public static bool TryGetLibrarySection(string? value, out string section)
    {
        section = "history";
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string input = value.Trim();
        if (input.Equals("feather://library", StringComparison.OrdinalIgnoreCase) ||
            input.Equals("feather://history", StringComparison.OrdinalIgnoreCase))
        {
            section = "history";
            return true;
        }
        if (input.Equals("feather://downloads", StringComparison.OrdinalIgnoreCase))
        {
            section = "downloads";
            return true;
        }
        if (input.Equals("feather://favorites", StringComparison.OrdinalIgnoreCase) ||
            input.Equals("feather://bookmarks", StringComparison.OrdinalIgnoreCase))
        {
            section = "favorites";
            return true;
        }
        return false;
    }

    public static string GetLibraryTitle(string section) => section switch
    {
        "downloads" => "Downloads",
        "favorites" => "Favorites",
        _ => "History"
    };
}
