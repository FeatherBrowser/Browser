namespace FeatherBrowser.Domain.Models;

internal static class UrlIdentity
{
    public static readonly IEqualityComparer<string> Comparer = new UrlIdentityComparer();

    public static string Key(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return string.Empty;

        string trimmed = url.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri) || string.IsNullOrEmpty(uri.Host))
            return trimmed;

        string authority = uri.IsDefaultPort
            ? uri.Host.ToLowerInvariant()
            : $"{uri.Host.ToLowerInvariant()}:{uri.Port}";
        string userInfo = string.IsNullOrEmpty(uri.UserInfo) ? string.Empty : uri.UserInfo + "@";

        return $"{uri.Scheme.ToLowerInvariant()}://{userInfo}{authority}{uri.GetComponents(UriComponents.PathAndQuery | UriComponents.Fragment, UriFormat.UriEscaped)}";
    }

    public static bool Equals(string? left, string? right) =>
        string.Equals(Key(left), Key(right), StringComparison.Ordinal);

    private sealed class UrlIdentityComparer : IEqualityComparer<string>
    {
        public bool Equals(string? x, string? y) => UrlIdentity.Equals(x, y);

        public int GetHashCode(string obj) => StringComparer.Ordinal.GetHashCode(Key(obj));
    }
}
