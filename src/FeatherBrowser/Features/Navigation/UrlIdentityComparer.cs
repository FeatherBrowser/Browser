namespace FeatherBrowser.Features.Navigation;

internal sealed class UrlIdentityComparer : IEqualityComparer<string>
{
    public static UrlIdentityComparer Instance { get; } = new();

    private UrlIdentityComparer()
    {
    }

    public bool Equals(string? left, string? right)
    {
        if (ReferenceEquals(left, right))
            return true;

        if (left is null || right is null)
            return false;

        if (!TryParseWebUri(left, out Uri? leftUri) ||
            !TryParseWebUri(right, out Uri? rightUri))
        {
            return string.Equals(left, right, StringComparison.Ordinal);
        }

        return string.Equals(leftUri.Scheme, rightUri.Scheme, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(leftUri.IdnHost, rightUri.IdnHost, StringComparison.OrdinalIgnoreCase) &&
               leftUri.Port == rightUri.Port &&
               string.Equals(leftUri.UserInfo, rightUri.UserInfo, StringComparison.Ordinal) &&
               string.Equals(leftUri.AbsolutePath, rightUri.AbsolutePath, StringComparison.Ordinal) &&
               string.Equals(leftUri.Query, rightUri.Query, StringComparison.Ordinal) &&
               string.Equals(leftUri.Fragment, rightUri.Fragment, StringComparison.Ordinal);
    }

    public int GetHashCode(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (!TryParseWebUri(value, out Uri? uri))
            return StringComparer.Ordinal.GetHashCode(value);

        var hash = new HashCode();
        hash.Add(uri.Scheme, StringComparer.OrdinalIgnoreCase);
        hash.Add(uri.IdnHost, StringComparer.OrdinalIgnoreCase);
        hash.Add(uri.Port);
        hash.Add(uri.UserInfo, StringComparer.Ordinal);
        hash.Add(uri.AbsolutePath, StringComparer.Ordinal);
        hash.Add(uri.Query, StringComparer.Ordinal);
        hash.Add(uri.Fragment, StringComparer.Ordinal);
        return hash.ToHashCode();
    }

    private static bool TryParseWebUri(string value, out Uri? uri)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out uri))
            return false;

        return string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }
}
