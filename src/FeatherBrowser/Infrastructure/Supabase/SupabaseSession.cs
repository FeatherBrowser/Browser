namespace FeatherBrowser.Infrastructure.Supabase;

internal sealed class SupabaseSession
{
    public required string UserId { get; init; }
    public required string AccessToken { get; init; }
    public required string RefreshToken { get; init; }
    public string Email { get; init; } = "";
    public DateTimeOffset ExpiresAt { get; init; }

    public bool NeedsRefresh =>
        DateTimeOffset.UtcNow >= ExpiresAt.Subtract(TimeSpan.FromMinutes(1));
}

internal sealed class SupabaseAuthResult
{
    public string? UserId { get; init; }
    public SupabaseSession? Session { get; init; }
    public bool RequiresEmailConfirmation { get; init; }
}
