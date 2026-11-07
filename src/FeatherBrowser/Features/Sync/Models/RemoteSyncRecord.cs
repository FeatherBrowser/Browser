namespace FeatherBrowser.Features.Sync.Models;

internal sealed class RemoteSyncRecord
{
    public required string UserId { get; init; }
    public int FormatVersion { get; init; }
    public long BlobRevision { get; init; }
    public required SyncKeyBundle KeyBundle { get; init; }
    public required EncryptedSyncBlob EncryptedBlob { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}
