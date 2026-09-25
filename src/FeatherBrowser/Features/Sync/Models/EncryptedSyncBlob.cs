namespace FeatherBrowser.Features.Sync.Models;

internal sealed class EncryptedSyncBlob
{
    public int Version { get; init; } = 1;
    public string Cipher { get; init; } = "AES-256-GCM";
    public string Nonce { get; init; } = "";
    public string Tag { get; init; } = "";
    public string Ciphertext { get; init; } = "";
}
