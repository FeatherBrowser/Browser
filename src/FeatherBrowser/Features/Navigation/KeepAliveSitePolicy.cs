namespace FeatherBrowser.Features.Navigation;

internal static class KeepAliveSitePolicy
{
    public static bool Matches(string host, string? rule)
    {
        string normalizedHost = NormalizeHost(host);
        string normalizedRule = NormalizeHost(rule);

        return normalizedHost.Length > 0 &&
               normalizedRule.Length > 0 &&
               (normalizedHost.Equals(normalizedRule, StringComparison.OrdinalIgnoreCase) ||
                normalizedHost.EndsWith('.' + normalizedRule, StringComparison.OrdinalIgnoreCase));
    }

    public static List<string> MatchingRules(string host, IEnumerable<string> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        return rules
            .Where(rule => Matches(host, rule))
            .ToList();
    }

    public static string NormalizeHost(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string host = value.Trim().TrimStart('*', '.');

        if (Uri.TryCreate(host, UriKind.Absolute, out Uri? uri) &&
            !string.IsNullOrWhiteSpace(uri.Host))
        {
            host = uri.Host;
        }

        return host.Trim().TrimEnd('/').ToLowerInvariant();
    }
}
