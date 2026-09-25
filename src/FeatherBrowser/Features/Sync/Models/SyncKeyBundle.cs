namespace FeatherBrowser.Features.Sync.Models;

internal sealed class SyncKeyBundle
{
    public int Version { get; init; } = 1;
    public string Cipher { get; init; } = "AES-256-GCM";
    public string Kdf { get; init; } = "HKDF-SHA256";
    public string RecoverySalt { get; init; } = "";
    public string DataSalt { get; init; } = "";
    public string Nonce { get; init; } = "";
    public string Tag { get; init; } = "";
    public string WrappedMasterKey { get; init; } = "";
}
