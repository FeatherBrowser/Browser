namespace FeatherBrowser.Infrastructure.Supabase;

internal sealed class TotpEnrollment
{
    public required string FactorId { get; init; }
    public string FriendlyName { get; init; } = "";
    public string Secret { get; init; } = "";
    public string Uri { get; init; } = "";
    public string QrCodeSvg { get; init; } = "";
}

internal sealed class MfaFactor
{
    public required string Id { get; init; }
    public string Type { get; init; } = "";
    public string Status { get; init; } = "";
    public string FriendlyName { get; init; } = "";

    public bool IsVerifiedTotp =>
        string.Equals(Type, "totp", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Status, "verified", StringComparison.OrdinalIgnoreCase);
}

internal sealed class MfaChallenge
{
    public required string ChallengeId { get; init; }
}

internal sealed class MfaRequiredException : Exception
{
    public MfaRequiredException(string message) : base(message)
    {
    }
}
