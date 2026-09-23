namespace FeatherBrowser.Domain.Models;

internal sealed class SavedCredential
{
    public string Name { get; set; } = "";
    public string Origin { get; set; } = "";
    public string Username { get; set; } = "";
    public string ProtectedPassword { get; set; } = "";
    public DateTimeOffset ImportedAt { get; set; } = DateTimeOffset.Now;
}
