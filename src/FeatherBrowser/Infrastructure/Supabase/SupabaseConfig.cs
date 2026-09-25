namespace FeatherBrowser.Infrastructure.Supabase;

internal sealed class SupabaseConfig
{
    public Uri BaseUri { get; }
    public string PublishableKey { get; }

    public SupabaseConfig(string projectUrl, string publishableKey)
    {
        if (!Uri.TryCreate(projectUrl, UriKind.Absolute, out Uri? uri))
            throw new ArgumentException("The Supabase project URL is invalid.", nameof(projectUrl));

        bool secure =
            string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

        bool loopback =
            uri.IsLoopback &&
            string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);

        if (!secure && !loopback)
            throw new ArgumentException(
                "Feather Sync requires HTTPS. Plain HTTP is allowed only for loopback development.",
                nameof(projectUrl));

        if (string.IsNullOrWhiteSpace(publishableKey))
            throw new ArgumentException("A Supabase publishable key is required.", nameof(publishableKey));

        string normalized = uri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? uri.AbsoluteUri
            : uri.AbsoluteUri + "/";

        BaseUri = new Uri(normalized, UriKind.Absolute);
        PublishableKey = publishableKey.Trim();
    }
}
