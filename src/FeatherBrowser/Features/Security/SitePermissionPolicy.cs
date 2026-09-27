namespace FeatherBrowser.Features.Security;

internal static class SitePermissionPolicy
{
    public static bool TryNormalizeOrigin(string? value, out string origin)
    {
        origin = string.Empty;

        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri))
            return false;

        string scheme = uri.Scheme.ToLowerInvariant();
        if (scheme is not "http" and not "https")
            return false;

        string host = uri.IdnHost.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(host))
            return false;

        string formattedHost = uri.HostNameType == UriHostNameType.IPv6
            ? $"[{host}]"
            : host;

        int defaultPort = scheme == "https" ? 443 : 80;
        origin = uri.Port == defaultPort
            ? $"{scheme}://{formattedHost}"
            : $"{scheme}://{formattedHost}:{uri.Port}";

        return true;
    }

    public static string PermissionKey(string origin, string kind) =>
        $"{origin.Trim().ToLowerInvariant()}|{kind.Trim().ToLowerInvariant()}";
}
