using System.Security.Cryptography;
using FeatherBrowser.Features.Sync.Models;
using FeatherBrowser.Infrastructure.Persistence;
using FeatherBrowser.Infrastructure.Supabase;

namespace FeatherBrowser.Features.Sync;

internal sealed class SyncCoordinator : IDisposable
{
    private readonly BrowserDataStore _store;
    private readonly ZeroKnowledgeSyncService _sync;
    private readonly SyncSnapshotService _snapshots;
    private readonly SyncLocalStateStore _state;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SyncCoordinator(
        BrowserDataStore store,
        ZeroKnowledgeSyncService sync,
        SyncSnapshotService snapshots,
        SyncLocalStateStore state)
    {
        _store = store;
        _sync = sync;
        _snapshots = snapshots;
        _state = state;
    }

    public byte[] CreateInitialSnapshot()
    {
        SyncSnapshot snapshot = _snapshots.Capture(_store);
        return _snapshots.Serialize(snapshot);
    }

    public void MarkInitialized(string userId, long revision, ReadOnlySpan<byte> plaintext)
    {
        SyncSnapshot snapshot = _snapshots.Deserialize(plaintext);
        _state.Save(userId, revision, snapshot, _snapshots);
    }

    public void ApplyProvisionedSnapshot(string userId, long revision, ReadOnlySpan<byte> plaintext)
    {
        SyncSnapshot snapshot = _snapshots.Deserialize(plaintext);
        _snapshots.Apply(_store, snapshot);
        _state.Save(userId, revision, snapshot, _snapshots);
    }

    public SyncLocalState? GetState(string userId) => _state.Load(userId);

    public async Task<SyncRunResult> SyncAsync(
        SupabaseSession session,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            SyncSnapshot local = _snapshots.Capture(_store);
            SyncLocalState? baseline = _state.Load(session.UserId);

            for (int attempt = 0; attempt < 3; attempt++)
            {
                SyncPullResult pulled = await _sync.PullWithRevisionAsync(session, cancellationToken);
                SyncSnapshot remote = _snapshots.Deserialize(pulled.Plaintext);
                SyncSnapshot merged = _snapshots.Merge(local, remote, baseline);
                byte[] mergedBytes = _snapshots.Serialize(merged);
                byte[] remoteBytes = pulled.Plaintext;

                try
                {
                    _snapshots.Apply(_store, merged);

                    string mergedHash = _snapshots.HashBytes(mergedBytes);
                    string remoteHash = _snapshots.HashBytes(remoteBytes);

                    if (string.Equals(mergedHash, remoteHash, StringComparison.Ordinal))
                    {
                        _state.Save(session.UserId, pulled.Revision, merged, _snapshots);
                        return new SyncRunResult(pulled.Revision, false, DateTimeOffset.UtcNow);
                    }

                    try
                    {
                        long revision = await _sync.PushAsync(
                            session,
                            mergedBytes,
                            pulled.Revision,
                            cancellationToken);

                        _state.Save(session.UserId, revision, merged, _snapshots);
                        return new SyncRunResult(revision, true, DateTimeOffset.UtcNow);
                    }
                    catch (SyncRevisionConflictException) when (attempt < 2)
                    {
                        continue;
                    }
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(mergedBytes);
                    CryptographicOperations.ZeroMemory(remoteBytes);
                }
            }

            throw new SyncRevisionConflictException();
        }
        finally
        {
            _gate.Release();
        }
    }

    public void DeleteLocalState() => _state.Delete();

    public void Dispose() => _gate.Dispose();
}

internal sealed record SyncRunResult(
    long Revision,
    bool Uploaded,
    DateTimeOffset CompletedAt);
