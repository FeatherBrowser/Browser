using System.Security.Cryptography;
using FeatherBrowser.Features.Sync.Crypto;
using FeatherBrowser.Features.Sync.Models;
using FeatherBrowser.Infrastructure.Supabase;

namespace FeatherBrowser.Features.Sync;

internal sealed class ZeroKnowledgeSyncService
{
    private readonly SupabaseSyncClient _remote;
    private readonly SyncDeviceSecretStore _secrets;

    public ZeroKnowledgeSyncService(
        SupabaseSyncClient remote,
        SyncDeviceSecretStore secrets)
    {
        _remote = remote;
        _secrets = secrets;
    }

    public async Task<SyncInitializationResult> InitializeAsync(
        SupabaseSession session,
        ReadOnlyMemory<byte> initialPlaintext,
        CancellationToken cancellationToken = default)
    {
        RemoteSyncRecord? existing =
            await _remote.GetAsync(session, cancellationToken);

        if (existing is not null)
        {
            throw new InvalidOperationException(
                "This Feather account already has encrypted sync data. Recover it with the account's Feather recovery key.");
        }

        byte[] masterKey = SyncCrypto.GenerateMasterKey();
        (string recoveryCode, byte[] recoveryKey) = RecoveryCode.Create();

        try
        {
            SyncKeyBundle bundle =
                SyncCrypto.CreateKeyBundle(
                    session.UserId,
                    masterKey,
                    recoveryKey);

            const long firstRevision = 1;

            EncryptedSyncBlob blob =
                SyncCrypto.Encrypt(
                    session.UserId,
                    firstRevision,
                    bundle,
                    masterKey,
                    initialPlaintext.Span);

            long committedRevision =
                await _remote.CommitAsync(
                    session,
                    expectedRevision: 0,
                    bundle,
                    blob,
                    cancellationToken);

            if (committedRevision != firstRevision)
                throw new SyncSecurityException("The sync server returned an unexpected initial revision.");

            string fingerprint = SyncCrypto.Fingerprint(bundle);

            _secrets.Save(
                session.UserId,
                masterKey,
                fingerprint,
                committedRevision);

            return new SyncInitializationResult(
                recoveryCode,
                committedRevision);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(recoveryKey);
            CryptographicOperations.ZeroMemory(masterKey);
        }
    }

    public async Task<byte[]> RecoverAsync(
        SupabaseSession session,
        string recoveryCode,
        CancellationToken cancellationToken = default)
    {
        RemoteSyncRecord remote =
            await _remote.GetAsync(session, cancellationToken)
            ?? throw new InvalidOperationException(
                "This Feather account has no sync data.");

        EnsureRemoteIdentity(session, remote);

        byte[] recoveryKey = RecoveryCode.Parse(recoveryCode);
        byte[]? masterKey = null;

        try
        {
            masterKey =
                SyncCrypto.UnwrapMasterKey(
                    session.UserId,
                    remote.KeyBundle,
                    recoveryKey);

            byte[] plaintext =
                SyncCrypto.Decrypt(
                    session.UserId,
                    remote.BlobRevision,
                    remote.KeyBundle,
                    masterKey,
                    remote.EncryptedBlob);

            string fingerprint =
                SyncCrypto.Fingerprint(remote.KeyBundle);

            _secrets.Save(
                session.UserId,
                masterKey,
                fingerprint,
                remote.BlobRevision);

            return plaintext;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(recoveryKey);

            if (masterKey is not null)
                CryptographicOperations.ZeroMemory(masterKey);
        }
    }

    public async Task<byte[]> PullAsync(
        SupabaseSession session,
        CancellationToken cancellationToken = default)
    {
        SyncPullResult result = await PullWithRevisionAsync(session, cancellationToken);
        return result.Plaintext;
    }

    public async Task<SyncPullResult> PullWithRevisionAsync(
        SupabaseSession session,
        CancellationToken cancellationToken = default)
    {
        using SyncDeviceSecrets local =
            LoadAndValidateLocalSecrets(session);

        RemoteSyncRecord remote =
            await _remote.GetAsync(session, cancellationToken)
            ?? throw new InvalidOperationException(
                "This Feather account has no sync data.");

        ValidateRemoteAgainstLocal(local, session, remote);

        byte[] plaintext =
            SyncCrypto.Decrypt(
                session.UserId,
                remote.BlobRevision,
                remote.KeyBundle,
                local.MasterKey,
                remote.EncryptedBlob);

        _secrets.Save(
            session.UserId,
            local.MasterKey,
            local.KeyBundleFingerprint,
            Math.Max(local.HighestSeenRevision, remote.BlobRevision));

        return new SyncPullResult(plaintext, remote.BlobRevision);
    }

    public async Task<long> PushAsync(
        SupabaseSession session,
        ReadOnlyMemory<byte> plaintext,
        CancellationToken cancellationToken = default)
    {
        using SyncDeviceSecrets local =
            LoadAndValidateLocalSecrets(session);

        RemoteSyncRecord remote =
            await _remote.GetAsync(session, cancellationToken)
            ?? throw new InvalidOperationException(
                "This Feather account has no sync data.");

        ValidateRemoteAgainstLocal(local, session, remote);

        long nextRevision =
            checked(remote.BlobRevision + 1);

        EncryptedSyncBlob blob =
            SyncCrypto.Encrypt(
                session.UserId,
                nextRevision,
                remote.KeyBundle,
                local.MasterKey,
                plaintext.Span);

        long committed =
            await _remote.CommitAsync(
                session,
                remote.BlobRevision,
                remote.KeyBundle,
                blob,
                cancellationToken);

        if (committed != nextRevision)
            throw new SyncSecurityException("The sync server returned an unexpected revision.");

        _secrets.Save(
            session.UserId,
            local.MasterKey,
            local.KeyBundleFingerprint,
            committed);

        return committed;
    }

    public async Task<long> PushAsync(
        SupabaseSession session,
        ReadOnlyMemory<byte> plaintext,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        using SyncDeviceSecrets local =
            LoadAndValidateLocalSecrets(session);

        RemoteSyncRecord remote =
            await _remote.GetAsync(session, cancellationToken)
            ?? throw new InvalidOperationException(
                "This Feather account has no sync data.");

        ValidateRemoteAgainstLocal(local, session, remote);

        if (remote.BlobRevision != expectedRevision)
            throw new SyncRevisionConflictException();

        long nextRevision = checked(expectedRevision + 1);

        EncryptedSyncBlob blob =
            SyncCrypto.Encrypt(
                session.UserId,
                nextRevision,
                remote.KeyBundle,
                local.MasterKey,
                plaintext.Span);

        long committed =
            await _remote.CommitAsync(
                session,
                expectedRevision,
                remote.KeyBundle,
                blob,
                cancellationToken);

        if (committed != nextRevision)
            throw new SyncSecurityException("The sync server returned an unexpected revision.");

        _secrets.Save(
            session.UserId,
            local.MasterKey,
            local.KeyBundleFingerprint,
            committed);

        return committed;
    }

    public async Task DeleteRemoteAsync(
        SupabaseSession session,
        CancellationToken cancellationToken = default)
    {
        await _remote.DeleteAsync(session, cancellationToken);
        _secrets.Delete();
    }

    private SyncDeviceSecrets LoadAndValidateLocalSecrets(
        SupabaseSession session)
    {
        SyncDeviceSecrets? local = _secrets.Load();

        if (local is null)
        {
            throw new InvalidOperationException(
                "This device has no Feather sync key. Recover the account with its Feather recovery key.");
        }

        if (!string.Equals(
                local.UserId,
                session.UserId,
                StringComparison.OrdinalIgnoreCase))
        {
            local.Dispose();
            throw new SyncSecurityException(
                "The local sync key belongs to a different Feather account.");
        }

        return local;
    }

    private static void ValidateRemoteAgainstLocal(
        SyncDeviceSecrets local,
        SupabaseSession session,
        RemoteSyncRecord remote)
    {
        EnsureRemoteIdentity(session, remote);

        if (remote.FormatVersion != 1)
            throw new SyncSecurityException("The remote Feather sync format is not supported.");

        string remoteFingerprint =
            SyncCrypto.Fingerprint(remote.KeyBundle);

        if (!string.Equals(
                remoteFingerprint,
                local.KeyBundleFingerprint,
                StringComparison.Ordinal))
        {
            throw new SyncSecurityException(
                "The server returned a different encrypted key bundle. Feather refused it instead of silently trusting the server.");
        }

        if (remote.BlobRevision < local.HighestSeenRevision)
        {
            throw new SyncSecurityException(
                "Feather detected a sync rollback: the server returned an older revision than this device has already seen.");
        }
    }

    private static void EnsureRemoteIdentity(
        SupabaseSession session,
        RemoteSyncRecord remote)
    {
        if (!string.Equals(
                session.UserId,
                remote.UserId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new SyncSecurityException(
                "The sync server returned data for a different account.");
        }
    }
}

internal sealed record SyncInitializationResult(
    string RecoveryCode,
    long Revision);

internal sealed record SyncPullResult(
    byte[] Plaintext,
    long Revision);
