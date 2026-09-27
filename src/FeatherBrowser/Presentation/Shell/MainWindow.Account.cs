using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using FeatherBrowser.Features.Sync;
using FeatherBrowser.Features.Sync.Devices;
using FeatherBrowser.Features.Sync.Models;
using FeatherBrowser.Infrastructure.Supabase;
using FeatherBrowser.Presentation.Account;

namespace FeatherBrowser.Presentation.Shell;

public partial class MainWindow : Window
{
    private async Task InitializeAccountAsync()
    {
        if (_isPrivateMode)
        {
            _accountPageState = AccountPageState.SignedOut(true);
            return;
        }

        await RunAccountOperationAsync(
            async () =>
            {
                SupabaseSession? stored = _supabaseSessionStore.Load();

                if (stored is null)
                {
                    _accountPageState = AccountPageState.SignedOut(false);
                    ConfigureAutomaticSync();
                    return;
                }

                _accountSession = stored;

                if (_accountSession.NeedsRefresh)
                {
                    SupabaseSession refreshed =
                        await _supabaseAuth.RefreshAsync(_accountSession.RefreshToken);

                    if (string.IsNullOrWhiteSpace(refreshed.Email) &&
                        !string.IsNullOrWhiteSpace(_accountSession.Email))
                    {
                        refreshed = new SupabaseSession
                        {
                            UserId = refreshed.UserId,
                            AccessToken = refreshed.AccessToken,
                            RefreshToken = refreshed.RefreshToken,
                            Email = _accountSession.Email,
                            ExpiresAt = refreshed.ExpiresAt
                        };
                    }

                    _accountSession = refreshed;
                    _supabaseSessionStore.Save(refreshed);
                }

                await RefreshAccountStateAsync();
                ConfigureAutomaticSync();

                if (CanRunAutomaticSync())
                    await SyncNowInternalAsync(false);
            },
            "initialize account");

        RefreshSettingsPageIfOpen();
    }

    private async Task SignInAccountAsync()
    {
        if (_isPrivateMode)
            return;

        AccountCredentials? credentials =
            AccountDialogs.ShowCredentials(this, false);

        if (credentials is null)
            return;

        await RunAccountOperationAsync(
            async () =>
            {
                SupabaseSession session =
                    await _supabaseAuth.SignInAsync(
                        credentials.Email,
                        credentials.Password);

                _accountSession = session;
                _supabaseSessionStore.Save(session);

                List<MfaFactor> factors =
                    await _supabaseMfa.ListFactorsAsync(session);

                MfaFactor? factor =
                    factors.FirstOrDefault(item => item.IsVerifiedTotp);

                if (factor is not null &&
                    !JwtAssuranceLevel.IsAal2(session.AccessToken))
                {
                    await VerifyMfaFactorAsync(factor, true);
                }

                await RefreshAccountStateAsync("Signed in to Feather.");

                ConfigureAutomaticSync();

                if (CanRunAutomaticSync())
                    await SyncNowInternalAsync(false);
            },
            "sign in");

        RefreshSettingsPageIfOpen();
    }

    private async Task SignUpAccountAsync()
    {
        if (_isPrivateMode)
            return;

        AccountCredentials? credentials =
            AccountDialogs.ShowCredentials(this, true);

        if (credentials is null)
            return;

        await RunAccountOperationAsync(
            async () =>
            {
                SupabaseAuthResult result =
                    await _supabaseAuth.SignUpAsync(
                        credentials.Email,
                        credentials.Password);

                if (result.Session is null)
                {
                    _accountSession = null;
                    _supabaseSessionStore.Delete();

                    _accountPageState =
                        AccountPageState.SignedOut(
                            false,
                            "Account created. Check your email to confirm it, then sign in.");
                }
                else
                {
                    _accountSession = result.Session;
                    _supabaseSessionStore.Save(result.Session);

                    await RefreshAccountStateAsync(
                        "Account created. Set up 2FA before enabling encrypted sync.");
                }
            },
            "sign up");

        RefreshSettingsPageIfOpen();
    }

    private async Task SignOutAccountAsync()
    {
        if (_isPrivateMode)
            return;

        SupabaseSession? session = _accountSession;

        _accountSession = null;
        _pendingTotpEnrollment = null;

        _supabaseSessionStore.Delete();
        _syncTimer.Stop();

        _accountPageState =
            AccountPageState.SignedOut(
                false,
                "Signed out. This device's encrypted sync key remains protected by Windows.");

        RefreshSettingsPageIfOpen();

        if (session is null)
            return;

        await RunAccountOperationAsync(
            () => _supabaseAuth.SignOutAsync(session.AccessToken),
            "remote sign out",
            false);
    }

    private async Task StartTotpEnrollmentAsync()
    {
        if (_accountSession is null || _isPrivateMode)
            return;

        await RunAccountOperationAsync(
            async () =>
            {
                List<MfaFactor> factors =
                    await _supabaseMfa.ListFactorsAsync(_accountSession);

                if (factors.Any(item => item.IsVerifiedTotp))
                {
                    await RefreshAccountStateAsync(
                        "2FA is already enabled for this account.");

                    return;
                }

                _pendingTotpEnrollment =
                    await _supabaseMfa.EnrollTotpAsync(
                        _accountSession,
                        Environment.MachineName);

                await RefreshAccountStateAsync(
                    "Scan the QR code with your authenticator app, then verify the six-digit code.");
            },
            "start MFA enrollment");

        RefreshSettingsPageIfOpen();
    }

    private async Task VerifyTotpEnrollmentAsync()
    {
        if (_accountSession is null ||
            _pendingTotpEnrollment is null ||
            _isPrivateMode)
        {
            return;
        }

        string? code =
            AccountDialogs.ShowCode(
                this,
                "Verify two-factor authentication",
                "Enter the six-digit code from your authenticator app.");

        if (code is null)
            return;

        await RunAccountOperationAsync(
            async () =>
            {
                SupabaseSession aal2 =
                    await _supabaseMfa.ChallengeAndVerifyAsync(
                        _accountSession,
                        _pendingTotpEnrollment.FactorId,
                        code);

                _accountSession = aal2;
                _supabaseSessionStore.Save(aal2);

                _pendingTotpEnrollment = null;

                await RefreshAccountStateAsync(
                    "Two-factor authentication is enabled.");

                ConfigureAutomaticSync();
            },
            "verify MFA enrollment");

        RefreshSettingsPageIfOpen();
    }

    private async Task VerifyExistingMfaAsync()
    {
        if (_accountSession is null || _isPrivateMode)
            return;

        await RunAccountOperationAsync(
            async () =>
            {
                List<MfaFactor> factors =
                    await _supabaseMfa.ListFactorsAsync(_accountSession);

                MfaFactor? factor =
                    factors.FirstOrDefault(item => item.IsVerifiedTotp);

                if (factor is null)
                {
                    await RefreshAccountStateAsync(
                        "Set up an authenticator before using encrypted sync.");

                    return;
                }

                await VerifyMfaFactorAsync(factor, true);

                await RefreshAccountStateAsync(
                    "2FA verified for this session.");

                ConfigureAutomaticSync();
            },
            "verify MFA");

        RefreshSettingsPageIfOpen();
    }

    private async Task<bool> VerifyMfaFactorAsync(
        MfaFactor factor,
        bool prompt)
    {
        if (_accountSession is null)
            return false;

        if (JwtAssuranceLevel.IsAal2(_accountSession.AccessToken))
            return true;

        if (!prompt)
            return false;

        string? code =
            AccountDialogs.ShowCode(
                this,
                "Two-factor authentication",
                "Enter the six-digit code from your authenticator app to continue.");

        if (code is null)
            return false;

        SupabaseSession aal2 =
            await _supabaseMfa.ChallengeAndVerifyAsync(
                _accountSession,
                factor.Id,
                code);

        _accountSession = aal2;
        _supabaseSessionStore.Save(aal2);

        return true;
    }

    private async Task<bool> EnsureAal2Async(bool prompt)
    {
        if (_accountSession is null || _isPrivateMode)
            return false;

        if (_accountSession.NeedsRefresh)
        {
            SupabaseSession old = _accountSession;

            SupabaseSession refreshed =
                await _supabaseAuth.RefreshAsync(old.RefreshToken);

            if (string.IsNullOrWhiteSpace(refreshed.Email))
            {
                refreshed = new SupabaseSession
                {
                    UserId = refreshed.UserId,
                    AccessToken = refreshed.AccessToken,
                    RefreshToken = refreshed.RefreshToken,
                    Email = old.Email,
                    ExpiresAt = refreshed.ExpiresAt
                };
            }

            _accountSession = refreshed;
            _supabaseSessionStore.Save(refreshed);
        }

        if (JwtAssuranceLevel.IsAal2(_accountSession.AccessToken))
            return true;

        List<MfaFactor> factors =
            await _supabaseMfa.ListFactorsAsync(_accountSession);

        MfaFactor? factor =
            factors.FirstOrDefault(item => item.IsVerifiedTotp);

        if (factor is null)
            return false;

        return await VerifyMfaFactorAsync(factor, prompt);
    }

    private async Task EnableEncryptedSyncAsync()
    {
        if (_isPrivateMode || _accountSession is null)
            return;

        byte[]? snapshot = null;

        try
        {
            await RunAccountOperationAsync(
                async () =>
                {
                    if (!await EnsureAal2Async(true))
                    {
                        await RefreshAccountStateAsync(
                            "2FA must be enabled and verified before encrypted sync can be created.");

                        return;
                    }

                    RemoteSyncRecord? remote =
                        await _supabaseSync.GetAsync(_accountSession);

                    if (remote is not null)
                    {
                        await RefreshAccountStateAsync(
                            "This account already has encrypted sync data. Approve this device or use the recovery key.");

                        return;
                    }

                    snapshot =
                        _syncCoordinator.CreateInitialSnapshot();

                    SyncInitializationResult result =
                        await _zeroKnowledgeSync.InitializeAsync(
                            _accountSession,
                            snapshot);

                    _syncCoordinator.MarkInitialized(
                        _accountSession.UserId,
                        result.Revision,
                        snapshot);

                    _store.Settings.SyncEnabled = true;
                    _store.SaveSettings();

                    AccountDialogs.ShowRecoveryKey(
                        this,
                        result.RecoveryCode);

                    await _deviceApproval.RegisterInitialDeviceAsync(
                        _accountSession,
                        Environment.MachineName);

                    ConfigureAutomaticSync();

                    await RefreshAccountStateAsync(
                        "Encrypted Feather Sync is active on this device.");
                },
                "enable encrypted sync");
        }
        finally
        {
            if (snapshot is not null)
                CryptographicOperations.ZeroMemory(snapshot);
        }

        RefreshSettingsPageIfOpen();
    }

    private async Task RecoverEncryptedSyncAsync()
    {
        if (_isPrivateMode || _accountSession is null)
            return;

        string? recoveryCode =
            AccountDialogs.ShowCode(
                this,
                "Recover Feather Sync",
                "Enter or paste your Feather recovery key. It is used locally and is never sent to Supabase.",
                true);

        if (recoveryCode is null)
            return;

        await RecoverEncryptedSyncWithCodeAsync(recoveryCode);
    }

    private async Task RecoverEncryptedSyncFromBackupAsync()
    {
        if (_isPrivateMode || _accountSession is null)
            return;

        string? recoveryCode =
            AccountDialogs.LoadRecoveryBackup(this);

        if (recoveryCode is null)
            return;

        await RecoverEncryptedSyncWithCodeAsync(recoveryCode);
    }

    private async Task RecoverEncryptedSyncWithCodeAsync(
        string recoveryCode)
    {
        if (_isPrivateMode || _accountSession is null)
            return;

        byte[]? plaintext = null;

        try
        {
            await RunAccountOperationAsync(
                async () =>
                {
                    if (!await EnsureAal2Async(true))
                    {
                        await RefreshAccountStateAsync(
                            "Verify 2FA before recovering encrypted sync data.");

                        return;
                    }

                    plaintext =
                        await _zeroKnowledgeSync.RecoverAsync(
                            _accountSession,
                            recoveryCode);

                    using SyncDeviceSecrets secrets =
                        _syncDeviceSecretStore.Load()
                        ?? throw new InvalidOperationException(
                            "Feather recovered the vault but could not save its device key.");

                    _syncCoordinator.ApplyProvisionedSnapshot(
                        _accountSession.UserId,
                        secrets.HighestSeenRevision,
                        plaintext);

                    await _deviceApproval.RegisterRecoveredDeviceAsync(
                        _accountSession,
                        Environment.MachineName);

                    _store.Settings.SyncEnabled = true;
                    _store.SaveSettings();

                    ApplyRuntimeSettings();
                    ConfigureAutomaticSync();

                    await RefreshAccountStateAsync(
                        "Recovery completed. This device is now approved for encrypted sync.");
                },
                "recover encrypted sync");
        }
        finally
        {
            if (plaintext is not null)
                CryptographicOperations.ZeroMemory(plaintext);
        }

        RefreshSettingsPageIfOpen();
    }

    private async Task RequestDeviceApprovalAsync()
    {
        if (_isPrivateMode || _accountSession is null)
            return;

        await RunAccountOperationAsync(
            async () =>
            {
                if (!await EnsureAal2Async(true))
                {
                    await RefreshAccountStateAsync(
                        "Verify 2FA before requesting device approval.");

                    return;
                }

                DeviceRegistration registration =
                    await _deviceApproval.RequestApprovalAsync(
                        _accountSession,
                        Environment.MachineName);

                await RefreshAccountStateAsync(
                    $"Approval requested. Compare pairing code {registration.PairingCode} on an existing device.");
            },
            "request device approval");

        RefreshSettingsPageIfOpen();
    }

    private async Task CheckDeviceApprovalAsync()
    {
        if (_isPrivateMode || _accountSession is null)
            return;

        DeviceApprovalResult? result = null;

        try
        {
            await RunAccountOperationAsync(
                async () =>
                {
                    if (!await EnsureAal2Async(true))
                        return;

                    result =
                        await _deviceApproval.TryAcceptAsync(
                            _accountSession);

                    if (result is null)
                    {
                        await RefreshAccountStateAsync(
                            "This device is still waiting for approval.");

                        return;
                    }

                    _syncCoordinator.ApplyProvisionedSnapshot(
                        _accountSession.UserId,
                        result.Revision,
                        result.PlaintextSnapshot);

                    _store.Settings.SyncEnabled = true;
                    _store.SaveSettings();

                    ApplyRuntimeSettings();
                    ConfigureAutomaticSync();

                    await RefreshAccountStateAsync(
                        "Device approved. Encrypted sync is now active.");
                },
                "check device approval");
        }
        finally
        {
            if (result is not null)
            {
                CryptographicOperations.ZeroMemory(
                    result.PlaintextSnapshot);
            }
        }

        RefreshSettingsPageIfOpen();
    }

    private async Task ApprovePendingDeviceAsync(
        string? deviceId)
    {
        if (_isPrivateMode ||
            _accountSession is null ||
            !Guid.TryParse(deviceId, out Guid id))
        {
            return;
        }

        await RunAccountOperationAsync(
            async () =>
            {
                if (!await EnsureAal2Async(true))
                    return;

                AccountDeviceState? device =
                    _accountPageState.Devices.FirstOrDefault(
                        item => item.Id == id.ToString());

                string detail =
                    device is null
                        ? id.ToString()
                        : $"{device.Name}\nPairing code: {device.PairingCode}";

                if (MessageBox.Show(
                        this,
                        $"Approve this Feather device?\n\n{detail}\n\nOnly approve it if the pairing code matches the code shown on the new device.",
                        "Approve Feather device",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question) != MessageBoxResult.Yes)
                {
                    return;
                }

                await _deviceApproval.ApproveAsync(
                    _accountSession,
                    id);

                await RefreshAccountStateAsync(
                    "The device was approved.");
            },
            "approve device");

        RefreshSettingsPageIfOpen();
    }

    private async Task DenyPendingDeviceAsync(
        string? deviceId)
    {
        if (_isPrivateMode ||
            _accountSession is null ||
            !Guid.TryParse(deviceId, out Guid id))
        {
            return;
        }

        await RunAccountOperationAsync(
            async () =>
            {
                if (!await EnsureAal2Async(true))
                    return;

                await _supabaseDevices.DenyDeviceAsync(
                    _accountSession,
                    id);

                await RefreshAccountStateAsync(
                    "The pending device request was removed.");
            },
            "deny device");

        RefreshSettingsPageIfOpen();
    }

    private async Task SyncNowAsync()
    {
        if (_isPrivateMode || _accountSession is null)
            return;

        await RunAccountOperationAsync(
            async () =>
            {
                if (!await EnsureAal2Async(true))
                {
                    await RefreshAccountStateAsync(
                        "Verify 2FA before syncing.");

                    return;
                }

                await SyncNowInternalAsync(true);
            },
            "manual sync");

        RefreshSettingsPageIfOpen();
    }

    private async Task SyncNowInternalAsync(
        bool refreshUi)
    {
        if (_syncBusy ||
            _accountSession is null ||
            _isPrivateMode ||
            !_store.Settings.SyncEnabled)
        {
            return;
        }

        if (!JwtAssuranceLevel.IsAal2(
                _accountSession.AccessToken))
        {
            return;
        }

        if (!HasLocalSyncKey(_accountSession.UserId))
            return;

        _syncBusy = true;

        try
        {
            SaveSessionSnapshot();

            SyncRunResult result =
                await _syncCoordinator.SyncAsync(
                    _accountSession);

            ApplyRuntimeSettings();

            StatusText.Text =
                result.Uploaded
                    ? "Feather Sync updated"
                    : "Feather Sync is up to date";

            if (refreshUi)
            {
                await RefreshAccountStateAsync(
                    result.Uploaded
                        ? "Encrypted sync uploaded successfully."
                        : "Encrypted sync is already up to date.");
            }
        }
        finally
        {
            _syncBusy = false;
        }
    }

    private async Task DeleteEncryptedSyncAsync()
    {
        if (_isPrivateMode || _accountSession is null)
            return;

        if (MessageBox.Show(
                this,
                "Delete all encrypted Feather Sync data and registered devices from the server? Local browser data will remain on this computer.",
                "Delete Feather Sync data",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        await RunAccountOperationAsync(
            async () =>
            {
                if (!await EnsureAal2Async(true))
                    return;

                await _zeroKnowledgeSync.DeleteRemoteAsync(
                    _accountSession);

                _syncCoordinator.DeleteLocalState();

                _store.Settings.SyncEnabled = false;
                _store.SaveSettings();

                ConfigureAutomaticSync();

                await RefreshAccountStateAsync(
                    "Encrypted cloud sync data was deleted. Local browsing data was not removed.");
            },
            "delete encrypted sync");

        RefreshSettingsPageIfOpen();
    }

    private async Task RefreshAccountStateAsync(
        string message = "")
    {
        if (_isPrivateMode)
        {
            _accountPageState =
                AccountPageState.SignedOut(
                    true,
                    message);

            return;
        }

        SupabaseSession? session = _accountSession;

        if (session is null)
        {
            _accountPageState =
                AccountPageState.SignedOut(
                    false,
                    message);

            return;
        }

        string assurance =
            JwtAssuranceLevel.Read(
                session.AccessToken);

        List<MfaFactor> factors = [];
        List<RemoteDevice> remoteDevices = [];

        bool remoteExists = false;

        string stateMessage = message;

        string? factorError =
            await RunAccountOperationAsync(
                async () =>
                {
                    factors =
                        await _supabaseMfa.ListFactorsAsync(
                            session);
                },
                "refresh MFA state",
                false);

        if (stateMessage.Length == 0 &&
            factorError is not null)
        {
            stateMessage = factorError;
        }

        bool hasMfa =
            factors.Any(item => item.IsVerifiedTotp);

        bool aal2 =
            string.Equals(
                assurance,
                "aal2",
                StringComparison.Ordinal);

        bool hasLocalKey =
            HasLocalSyncKey(session.UserId);

        if (aal2)
        {
            string? remoteError =
                await RunAccountOperationAsync(
                    async () =>
                    {
                        remoteExists =
                            await _supabaseSync.GetAsync(session)
                            is not null;

                        remoteDevices =
                            await _supabaseDevices.ListDevicesAsync(
                                session);
                    },
                    "refresh sync state",
                    false);

            if (stateMessage.Length == 0 &&
                remoteError is not null)
            {
                stateMessage = remoteError;
            }
        }

        Guid? currentDeviceId = null;
        string pairingCode = "";

        string? deviceError =
            await RunAccountOperationAsync(
                () =>
                {
                    using DeviceIdentity? identity =
                        _deviceIdentityStore.Load();

                    if (identity is null)
                        return Task.CompletedTask;

                    currentDeviceId =
                        identity.DeviceId;

                    RemoteDevice? current =
                        remoteDevices.FirstOrDefault(
                            device =>
                                device.DeviceId ==
                                identity.DeviceId);

                    if (current is not null &&
                        string.Equals(
                            current.Status,
                            "pending",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        pairingCode =
                            current.PairingCode;
                    }

                    return Task.CompletedTask;
                },
                "load device identity",
                false);

        if (stateMessage.Length == 0 &&
            deviceError is not null)
        {
            stateMessage = deviceError;
        }

        SyncLocalState? localState = null;

        string? localStateError =
            await RunAccountOperationAsync(
                () =>
                {
                    localState =
                        _syncCoordinator.GetState(
                            session.UserId);

                    return Task.CompletedTask;
                },
                "load sync state",
                false);

        if (stateMessage.Length == 0 &&
            localStateError is not null)
        {
            stateMessage = localStateError;
        }

        string lastSync =
            localState is null
                ? "Never"
                : localState.LastSyncUtc
                    .ToLocalTime()
                    .ToString("g");

        string syncStatus =
            !hasMfa
                ? "2FA setup required"
                : !aal2
                    ? "2FA verification required"
                    : !remoteExists
                        ? "Ready to enable encrypted sync"
                        : !hasLocalKey
                            ? pairingCode.Length > 0
                                ? "Waiting for device approval"
                                : "Recovery or device approval required"
                            : _store.Settings.SyncEnabled
                                ? "Encrypted sync active"
                                : "Encrypted sync available";

        _accountPageState =
            new AccountPageState
            {
                IsSignedIn = true,
                Email = session.Email,
                UserId = session.UserId,
                AssuranceLevel = assurance,
                HasVerifiedMfa = hasMfa,
                HasLocalSyncKey = hasLocalKey,
                RemoteSyncExists = remoteExists,
                SyncEnabled =
                    _store.Settings.SyncEnabled &&
                    hasLocalKey &&
                    remoteExists,
                CanApproveDevices =
                    aal2 &&
                    hasLocalKey,
                TotpSetupActive =
                    _pendingTotpEnrollment is not null,
                TotpSecret =
                    _pendingTotpEnrollment?.Secret ?? "",
                TotpQrCodeSvg =
                    _pendingTotpEnrollment?.QrCodeSvg ?? "",
                PairingCode = pairingCode,
                SyncStatus = syncStatus,
                LastSync = lastSync,
                Message = stateMessage,
                Devices =
                    remoteDevices
                        .Select(
                            device =>
                                new AccountDeviceState
                                {
                                    Id =
                                        device.DeviceId.ToString(),
                                    Name =
                                        device.DeviceName,
                                    Status =
                                        device.Status,
                                    PairingCode =
                                        device.PairingCode,
                                    IsCurrent =
                                        currentDeviceId ==
                                        device.DeviceId
                                })
                        .ToList()
            };
    }

    private bool HasLocalSyncKey(
        string userId)
    {
        try
        {
            using SyncDeviceSecrets? secrets =
                _syncDeviceSecretStore.Load();

            return
                secrets is not null &&
                string.Equals(
                    secrets.UserId,
                    userId,
                    StringComparison.OrdinalIgnoreCase);
        }
        catch (SyncSecurityException exception)
        {
            Trace.TraceWarning(
                $"Feather local sync key could not be loaded: {exception}");

            return false;
        }
        catch (CryptographicException exception)
        {
            Trace.TraceWarning(
                $"Feather local sync key could not be decrypted: {exception}");

            return false;
        }
        catch (JsonException exception)
        {
            Trace.TraceWarning(
                $"Feather local sync key data is malformed: {exception}");

            return false;
        }
        catch (IOException exception)
        {
            Trace.TraceWarning(
                $"Feather local sync key could not be read: {exception}");

            return false;
        }
        catch (UnauthorizedAccessException exception)
        {
            Trace.TraceWarning(
                $"Feather local sync key access was denied: {exception}");

            return false;
        }
    }

    private bool CanRunAutomaticSync() =>
        !_isPrivateMode &&
        _accountSession is not null &&
        JwtAssuranceLevel.IsAal2(
            _accountSession.AccessToken) &&
        _store.Settings.SyncEnabled &&
        _store.Settings.AutoSyncEnabled &&
        HasLocalSyncKey(
            _accountSession.UserId);

    private void ConfigureAutomaticSync()
    {
        _syncTimer.Stop();

        _syncTimer.Interval =
            TimeSpan.FromMinutes(
                Math.Clamp(
                    _store.Settings.AutoSyncMinutes,
                    1,
                    60));

        if (CanRunAutomaticSync())
            _syncTimer.Start();
    }

    private async Task AutomaticSyncTickAsync()
    {
        if (_isPrivateMode ||
            _accountSession is null ||
            !_store.Settings.SyncEnabled ||
            !_store.Settings.AutoSyncEnabled)
        {
            ConfigureAutomaticSync();
            return;
        }

        string? error =
            await RunAccountOperationAsync(
                async () =>
                {
                    if (_accountSession.NeedsRefresh)
                    {
                        SupabaseSession old =
                            _accountSession;

                        SupabaseSession refreshed =
                            await _supabaseAuth.RefreshAsync(
                                old.RefreshToken);

                        if (string.IsNullOrWhiteSpace(
                                refreshed.Email))
                        {
                            refreshed =
                                new SupabaseSession
                                {
                                    UserId =
                                        refreshed.UserId,
                                    AccessToken =
                                        refreshed.AccessToken,
                                    RefreshToken =
                                        refreshed.RefreshToken,
                                    Email =
                                        old.Email,
                                    ExpiresAt =
                                        refreshed.ExpiresAt
                                };
                        }

                        _accountSession = refreshed;
                        _supabaseSessionStore.Save(
                            refreshed);
                    }

                    if (!CanRunAutomaticSync())
                    {
                        ConfigureAutomaticSync();
                        return;
                    }

                    await SyncNowInternalAsync(false);
                },
                "automatic sync",
                false);

        if (error is not null)
        {
            StatusText.Text =
                $"Feather Sync: {error}";
        }
    }

    private void RefreshSettingsPageIfOpen()
    {
        if (_activeTab?.IsSettingsPage == true)
            ShowSettingsPage(_activeTab);
    }

    private async Task<string?> RunAccountOperationAsync(
        Func<Task> operation,
        string operationName,
        bool refreshAccountStateOnFailure = true)
    {
        try
        {
            await operation();
            return null;
        }
        catch (SyncSecurityException exception)
        {
            return await HandleAccountOperationFailureAsync(
                exception,
                operationName,
                refreshAccountStateOnFailure);
        }
        catch (MfaRequiredException exception)
        {
            return await HandleAccountOperationFailureAsync(
                exception,
                operationName,
                refreshAccountStateOnFailure);
        }
        catch (SyncRevisionConflictException exception)
        {
            return await HandleAccountOperationFailureAsync(
                exception,
                operationName,
                refreshAccountStateOnFailure);
        }
        catch (HttpRequestException exception)
        {
            return await HandleAccountOperationFailureAsync(
                exception,
                operationName,
                refreshAccountStateOnFailure);
        }
        catch (TaskCanceledException exception)
        {
            return await HandleAccountOperationFailureAsync(
                exception,
                operationName,
                refreshAccountStateOnFailure);
        }
        catch (OperationCanceledException exception)
        {
            return await HandleAccountOperationFailureAsync(
                exception,
                operationName,
                refreshAccountStateOnFailure);
        }
        catch (TimeoutException exception)
        {
            return await HandleAccountOperationFailureAsync(
                exception,
                operationName,
                refreshAccountStateOnFailure);
        }
        catch (JsonException exception)
        {
            return await HandleAccountOperationFailureAsync(
                exception,
                operationName,
                refreshAccountStateOnFailure);
        }
        catch (CryptographicException exception)
        {
            return await HandleAccountOperationFailureAsync(
                exception,
                operationName,
                refreshAccountStateOnFailure);
        }
        catch (InvalidDataException exception)
        {
            return await HandleAccountOperationFailureAsync(
                exception,
                operationName,
                refreshAccountStateOnFailure);
        }
        catch (IOException exception)
        {
            return await HandleAccountOperationFailureAsync(
                exception,
                operationName,
                refreshAccountStateOnFailure);
        }
        catch (UnauthorizedAccessException exception)
        {
            return await HandleAccountOperationFailureAsync(
                exception,
                operationName,
                refreshAccountStateOnFailure);
        }
        catch (InvalidOperationException exception)
        {
            return await HandleAccountOperationFailureAsync(
                exception,
                operationName,
                refreshAccountStateOnFailure);
        }
        catch (ArgumentException exception)
        {
            return await HandleAccountOperationFailureAsync(
                exception,
                operationName,
                refreshAccountStateOnFailure);
        }
        catch (FormatException exception)
        {
            return await HandleAccountOperationFailureAsync(
                exception,
                operationName,
                refreshAccountStateOnFailure);
        }
    }

    private async Task<string> HandleAccountOperationFailureAsync(
        Exception exception,
        string operationName,
        bool refreshAccountState)
    {
        Trace.TraceWarning(
            $"Feather Account '{operationName}' failed: {exception}");

        string message =
            SafeAccountMessage(exception);

        if (refreshAccountState)
        {
            await RefreshAccountStateAsync(
                message);
        }

        return message;
    }

    private static string SafeAccountMessage(
        Exception exception) =>
        exception switch
        {
            SyncSecurityException =>
                exception.Message,

            MfaRequiredException =>
                exception.Message,

            SyncRevisionConflictException =>
                exception.Message,

            HttpRequestException =>
                exception.Message,

            TaskCanceledException =>
                "The Feather Account request timed out. Check your connection and try again.",

            OperationCanceledException =>
                "The Feather Account request was cancelled.",

            TimeoutException =>
                "The Feather Account request timed out. Check your connection and try again.",

            JsonException =>
                "Feather received invalid account or sync data.",

            CryptographicException =>
                "Windows could not protect or unlock Feather's local account data.",

            InvalidDataException =>
                exception.Message,

            UnauthorizedAccessException =>
                "Feather does not have permission to access its local account data.",

            IOException =>
                "Feather could not access its local account data.",

            InvalidOperationException =>
                exception.Message,

            ArgumentException =>
                exception.Message,

            FormatException =>
                "Feather received malformed account or sync data.",

            _ =>
                "Feather Account could not complete that request."
        };
}