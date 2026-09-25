using System.Security.Cryptography;
using FeatherBrowser.Features.Sync.Crypto;
using FeatherBrowser.Features.Sync.Models;
using FeatherBrowser.Infrastructure.Supabase;

namespace FeatherBrowser.Features.Sync.Devices;

internal sealed class DeviceApprovalService
{
    private readonly SupabaseDeviceClient _devices;
    private readonly SupabaseSyncClient _sync;
    private readonly DeviceIdentityStore _identityStore;
    private readonly SyncDeviceSecretStore _secretStore;

    public DeviceApprovalService(
        SupabaseDeviceClient devices,
        SupabaseSyncClient sync,
        DeviceIdentityStore identityStore,
        SyncDeviceSecretStore secretStore)
    {
        _devices = devices;
        _sync = sync;
        _identityStore = identityStore;
        _secretStore = secretStore;
    }

    public async Task<DeviceRegistration> RegisterInitialDeviceAsync(
        SupabaseSession session,
        string deviceName,
        CancellationToken cancellationToken = default)
    {
        SupabaseMfaClient.RequireAal2(session);

        using SyncDeviceSecrets secrets =
            LoadSecretsForSession(session);

        using DeviceIdentity identity =
            _identityStore.LoadOrCreate(
                deviceName);

        await _devices.RegisterFirstDeviceAsync(
            session,
            identity,
            cancellationToken);

        return new DeviceRegistration(
            identity.DeviceId,
            identity.DeviceName,
            DevicePairingCrypto.PairingCode(
                identity.PublicKey));
    }

    public async Task<DeviceRegistration> RegisterRecoveredDeviceAsync(
        SupabaseSession session,
        string deviceName,
        CancellationToken cancellationToken = default)
    {
        SupabaseMfaClient.RequireAal2(session);

        using SyncDeviceSecrets secrets =
            LoadSecretsForSession(session);

        using DeviceIdentity identity =
            _identityStore.LoadOrCreate(
                deviceName);

        await _devices.RegisterRecoveredDeviceAsync(
            session,
            identity,
            cancellationToken);

        return new DeviceRegistration(
            identity.DeviceId,
            identity.DeviceName,
            DevicePairingCrypto.PairingCode(
                identity.PublicKey));
    }

    public async Task<DeviceRegistration> RequestApprovalAsync(
        SupabaseSession session,
        string deviceName,
        CancellationToken cancellationToken = default)
    {
        SupabaseMfaClient.RequireAal2(session);

        using DeviceIdentity identity =
            _identityStore.LoadOrCreate(
                deviceName);

        await _devices.RegisterPendingDeviceAsync(
            session,
            identity,
            cancellationToken);

        return new DeviceRegistration(
            identity.DeviceId,
            identity.DeviceName,
            DevicePairingCrypto.PairingCode(
                identity.PublicKey));
    }

    public async Task<List<RemoteDevice>> GetPendingRequestsAsync(
        SupabaseSession session,
        CancellationToken cancellationToken = default)
    {
        SupabaseMfaClient.RequireAal2(session);

        using SyncDeviceSecrets secrets =
            LoadSecretsForSession(session);

        List<RemoteDevice> devices =
            await _devices.ListDevicesAsync(
                session,
                cancellationToken);

        return devices
            .Where(device =>
                string.Equals(
                    device.Status,
                    "pending",
                    StringComparison.Ordinal))
            .ToList();
    }

    public async Task ApproveAsync(
        SupabaseSession session,
        Guid targetDeviceId,
        CancellationToken cancellationToken = default)
    {
        SupabaseMfaClient.RequireAal2(session);

        using SyncDeviceSecrets secrets =
            LoadSecretsForSession(session);

        using DeviceIdentity source =
            _identityStore.Load()
            ?? throw new InvalidOperationException(
                "This Feather installation has no local device identity.");

        List<RemoteDevice> devices =
            await _devices.ListDevicesAsync(
                session,
                cancellationToken);

        RemoteDevice target =
            devices.FirstOrDefault(
                device =>
                    device.DeviceId == targetDeviceId &&
                    string.Equals(
                        device.Status,
                        "pending",
                        StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                "The pending Feather device could not be found.");

        DeviceKeyTransfer transfer =
            DevicePairingCrypto.EncryptMasterKey(
                session.UserId,
                source,
                target,
                secrets.KeyBundleFingerprint,
                secrets.MasterKey);

        await _devices.SubmitTransferAsync(
            session,
            transfer,
            cancellationToken);
    }

    public async Task<DeviceApprovalResult?> TryAcceptAsync(
        SupabaseSession session,
        CancellationToken cancellationToken = default)
    {
        SupabaseMfaClient.RequireAal2(session);

        using DeviceIdentity identity =
            _identityStore.Load()
            ?? throw new InvalidOperationException(
                "This Feather installation has no pending device identity.");

        DeviceKeyTransfer? transfer =
            await _devices.GetTransferAsync(
                session,
                identity.DeviceId,
                cancellationToken);

        if (transfer is null)
            return null;

        byte[] masterKey =
            DevicePairingCrypto.DecryptMasterKey(
                session.UserId,
                identity,
                transfer);

        try
        {
            RemoteSyncRecord remote =
                await _sync.GetAsync(
                    session,
                    cancellationToken)
                ?? throw new InvalidOperationException(
                    "This Feather account has no encrypted sync data.");

            if (!string.Equals(
                    remote.UserId,
                    session.UserId,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new SyncSecurityException(
                    "The server returned sync data for another account.");
            }

            string remoteFingerprint =
                SyncCrypto.Fingerprint(
                    remote.KeyBundle);

            if (!string.Equals(
                    remoteFingerprint,
                    transfer.KeyBundleFingerprint,
                    StringComparison.Ordinal))
            {
                throw new SyncSecurityException(
                    "The approved device transfer does not match the account's encrypted key bundle.");
            }

            byte[] plaintext =
                SyncCrypto.Decrypt(
                    session.UserId,
                    remote.BlobRevision,
                    remote.KeyBundle,
                    masterKey,
                    remote.EncryptedBlob);

            _secretStore.Save(
                session.UserId,
                masterKey,
                remoteFingerprint,
                remote.BlobRevision);

            await _devices.ConsumeTransferAsync(
                session,
                identity.DeviceId,
                cancellationToken);

            return new DeviceApprovalResult(
                plaintext,
                remote.BlobRevision);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(
                masterKey);
        }
    }

    private SyncDeviceSecrets LoadSecretsForSession(
        SupabaseSession session)
    {
        SyncDeviceSecrets? secrets =
            _secretStore.Load();

        if (secrets is null)
        {
            throw new InvalidOperationException(
                "This device does not have the Feather Sync Master Key.");
        }

        if (!string.Equals(
                secrets.UserId,
                session.UserId,
                StringComparison.OrdinalIgnoreCase))
        {
            secrets.Dispose();

            throw new SyncSecurityException(
                "The local Feather Sync Master Key belongs to another account.");
        }

        return secrets;
    }
}

internal sealed record DeviceRegistration(
    Guid DeviceId,
    string DeviceName,
    string PairingCode);

internal sealed record DeviceApprovalResult(
    byte[] PlaintextSnapshot,
    long Revision);
