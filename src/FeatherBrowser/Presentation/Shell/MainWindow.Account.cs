using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
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

        SupabaseSession? stored = _supabaseSessionStore.Load();
        if (stored is null)
        {
            _accountPageState = AccountPageState.SignedOut(false);
            ConfigureAutomaticSync();
            return;
        }

        _accountSession = stored;

        try
        {
            if (_accountSession.NeedsRefresh)
            {
                SupabaseSession refreshed = await _supabaseAuth.RefreshAsync(_accountSession.RefreshToken);
                if (string.IsNullOrWhiteSpace(refreshed.Email) && !string.IsNullOrWhiteSpace(_accountSession.Email))
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
        }
        catch (Exception exception)
        {
            await RefreshAccountStateAsync(SafeAccountMessage(exception));
        }

        RefreshSettingsPageIfOpen();
    }

    private async Task SignInAccountAsync()
    {
        if (_isPrivateMode)
            return;

        AccountCredentials? credentials = AccountDialogs.ShowCredentials(this, false);
        if (credentials is null)
            return;

        try
        {
            SupabaseSession session = await _supabaseAuth.SignInAsync(credentials.Email, credentials.Password);
            _accountSession = session;
            _supabaseSessionStore.Save(session);

            List<MfaFactor> factors = await _supabaseMfa.ListFactorsAsync(session);
            MfaFactor? factor = factors.FirstOrDefault(item => item.IsVerifiedTotp);

            if (factor is not null && !JwtAssuranceLevel.IsAal2(session.AccessToken))
                await VerifyMfaFactorAsync(factor, true);

            await RefreshAccountStateAsync("Signed in to Feather.");
            ConfigureAutomaticSync();

            if (CanRunAutomaticSync())
                await SyncNowInternalAsync(false);
        }
        catch (Exception exception)
        {
            await RefreshAccountStateAsync(SafeAccountMessage(exception));
        }

        RefreshSettingsPageIfOpen();
    }

    private async Task SignUpAccountAsync()
    {
        if (_isPrivateMode)
            return;

        AccountCredentials? credentials = AccountDialogs.ShowCredentials(this, true);
        if (credentials is null)
            return;

        try
        {
            SupabaseAuthResult result = await _supabaseAuth.SignUpAsync(credentials.Email, credentials.Password);

            if (result.Session is null)
            {
                _accountSession = null;
                _supabaseSessionStore.Delete();
                _accountPageState = AccountPageState.SignedOut(false, "Account created. Check your email to confirm it, then sign in.");
            }
            else
            {
                _accountSession = result.Session;
                _supabaseSessionStore.Save(result.Session);
                await RefreshAccountStateAsync("Account created. Set up 2FA before enabling encrypted sync.");
            }
        }
        catch (Exception exception)
        {
            await RefreshAccountStateAsync(SafeAccountMessage(exception));
        }

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

        if (session is not null)
        {
            try
            {
                await _supabaseAuth.SignOutAsync(session.AccessToken);
            }
            catch
            {
            }
        }

        _accountPageState = AccountPageState.SignedOut(false, "Signed out. This device's encrypted sync key remains protected by Windows.");
        RefreshSettingsPageIfOpen();
    }

    private async Task StartTotpEnrollmentAsync()
    {
        if (_accountSession is null || _isPrivateMode)
            return;

        try
        {
            List<MfaFactor> factors = await _supabaseMfa.ListFactorsAsync(_accountSession);
            if (factors.Any(item => item.IsVerifiedTotp))
            {
                await RefreshAccountStateAsync("2FA is already enabled for this account.");
                RefreshSettingsPageIfOpen();
                return;
            }

            _pendingTotpEnrollment = await _supabaseMfa.EnrollTotpAsync(
                _accountSession,
                Environment.MachineName);

            await RefreshAccountStateAsync("Scan the QR code with your authenticator app, then verify the six-digit code.");
        }
        catch (Exception exception)
        {
            await RefreshAccountStateAsync(SafeAccountMessage(exception));
        }

        RefreshSettingsPageIfOpen();
    }

    private async Task VerifyTotpEnrollmentAsync()
    {
        if (_accountSession is null || _pendingTotpEnrollment is null || _isPrivateMode)
            return;

        string? code = AccountDialogs.ShowCode(
            this,
            "Verify two-factor authentication",
            "Enter the six-digit code from your authenticator app.");

        if (code is null)
            return;

        try
        {
            SupabaseSession aal2 = await _supabaseMfa.ChallengeAndVerifyAsync(
                _accountSession,
                _pendingTotpEnrollment.FactorId,
                code);

            _accountSession = aal2;
            _supabaseSessionStore.Save(aal2);
            _pendingTotpEnrollment = null;
            await RefreshAccountStateAsync("Two-factor authentication is enabled.");
            ConfigureAutomaticSync();
        }
        catch (Exception exception)
        {
            await RefreshAccountStateAsync(SafeAccountMessage(exception));
        }

        RefreshSettingsPageIfOpen();
    }

    private async Task VerifyExistingMfaAsync()
    {
        if (_accountSession is null || _isPrivateMode)
            return;

        try
        {
            List<MfaFactor> factors = await _supabaseMfa.ListFactorsAsync(_accountSession);
            MfaFactor? factor = factors.FirstOrDefault(item => item.IsVerifiedTotp);

            if (factor is null)
            {
                await RefreshAccountStateAsync("Set up an authenticator before using encrypted sync.");
            }
            else
            {
                await VerifyMfaFactorAsync(factor, true);
                await RefreshAccountStateAsync("2FA verified for this session.");
                ConfigureAutomaticSync();
            }
        }
        catch (Exception exception)
        {
            await RefreshAccountStateAsync(SafeAccountMessage(exception));
        }

        RefreshSettingsPageIfOpen();
    }

    private async Task<bool> VerifyMfaFactorAsync(MfaFactor factor, bool prompt)
    {
        if (_accountSession is null)
            return false;

        if (JwtAssuranceLevel.IsAal2(_accountSession.AccessToken))
            return true;

        if (!prompt)
            return false;

        string? code = AccountDialogs.ShowCode(
            this,
            "Two-factor authentication",
            "Enter the six-digit code from your authenticator app to continue.");

        if (code is null)
            return false;

        SupabaseSession aal2 = await _supabaseMfa.ChallengeAndVerifyAsync(
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
            SupabaseSession refreshed = await _supabaseAuth.RefreshAsync(old.RefreshToken);
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

        List<MfaFactor> factors = await _supabaseMfa.ListFactorsAsync(_accountSession);
        MfaFactor? factor = factors.FirstOrDefault(item => item.IsVerifiedTotp);
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
            if (!await EnsureAal2Async(true))
            {
                await RefreshAccountStateAsync("2FA must be enabled and verified before encrypted sync can be created.");
                return;
            }

            RemoteSyncRecord? remote = await _supabaseSync.GetAsync(_accountSession);
            if (remote is not null)
            {
                await RefreshAccountStateAsync("This account already has encrypted sync data. Approve this device or use the recovery key.");
                return;
            }

            snapshot = _syncCoordinator.CreateInitialSnapshot();
            SyncInitializationResult result = await _zeroKnowledgeSync.InitializeAsync(_accountSession, snapshot);
            _syncCoordinator.MarkInitialized(_accountSession.UserId, result.Revision, snapshot);

            _store.Settings.SyncEnabled = true;
            _store.SaveSettings();

            AccountDialogs.ShowRecoveryKey(this, result.RecoveryCode);

            await _deviceApproval.RegisterInitialDeviceAsync(
                _accountSession,
                Environment.MachineName);

            ConfigureAutomaticSync();
            await RefreshAccountStateAsync("Encrypted Feather Sync is active on this device.");
        }
        catch (Exception exception)
        {
            await RefreshAccountStateAsync(SafeAccountMessage(exception));
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

        string? recoveryCode = AccountDialogs.ShowCode(
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

        string? recoveryCode = AccountDialogs.LoadRecoveryBackup(this);
        if (recoveryCode is null)
            return;

        await RecoverEncryptedSyncWithCodeAsync(recoveryCode);
    }

    private async Task RecoverEncryptedSyncWithCodeAsync(string recoveryCode)
    {
        if (_isPrivateMode || _accountSession is null)
            return;

        byte[]? plaintext = null;

        try
        {
            if (!await EnsureAal2Async(true))
            {
                await RefreshAccountStateAsync("Verify 2FA before recovering encrypted sync data.");
                return;
            }

            plaintext = await _zeroKnowledgeSync.RecoverAsync(_accountSession, recoveryCode);

            using SyncDeviceSecrets secrets = _syncDeviceSecretStore.Load()
                ?? throw new InvalidOperationException("Feather recovered the vault but could not save its device key.");

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
            await RefreshAccountStateAsync("Recovery completed. This device is now approved for encrypted sync.");
        }
        catch (Exception exception)
        {
            await RefreshAccountStateAsync(SafeAccountMessage(exception));
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

        try
        {
            if (!await EnsureAal2Async(true))
            {
                await RefreshAccountStateAsync("Verify 2FA before requesting device approval.");
                return;
            }

            DeviceRegistration registration = await _deviceApproval.RequestApprovalAsync(
                _accountSession,
                Environment.MachineName);

            await RefreshAccountStateAsync($"Approval requested. Compare pairing code {registration.PairingCode} on an existing device.");
        }
        catch (Exception exception)
        {
            await RefreshAccountStateAsync(SafeAccountMessage(exception));
        }

        RefreshSettingsPageIfOpen();
    }

    private async Task CheckDeviceApprovalAsync()
    {
        if (_isPrivateMode || _accountSession is null)
            return;

        DeviceApprovalResult? result = null;

        try
        {
            if (!await EnsureAal2Async(true))
                return;

            result = await _deviceApproval.TryAcceptAsync(_accountSession);
            if (result is null)
            {
                await RefreshAccountStateAsync("This device is still waiting for approval.");
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
            await RefreshAccountStateAsync("Device approved. Encrypted sync is now active.");
        }
        catch (Exception exception)
        {
            await RefreshAccountStateAsync(SafeAccountMessage(exception));
        }
        finally
        {
            if (result is not null)
                CryptographicOperations.ZeroMemory(result.PlaintextSnapshot);
        }

        RefreshSettingsPageIfOpen();
    }

    private async Task ApprovePendingDeviceAsync(string? deviceId)
    {
        if (_isPrivateMode || _accountSession is null || !Guid.TryParse(deviceId, out Guid id))
            return;

        try
        {
            if (!await EnsureAal2Async(true))
                return;

            AccountDeviceState? device = _accountPageState.Devices.FirstOrDefault(item => item.Id == id.ToString());
            string detail = device is null
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

            await _deviceApproval.ApproveAsync(_accountSession, id);
            await RefreshAccountStateAsync("The device was approved.");
        }
        catch (Exception exception)
        {
            await RefreshAccountStateAsync(SafeAccountMessage(exception));
        }

        RefreshSettingsPageIfOpen();
    }

    private async Task DenyPendingDeviceAsync(string? deviceId)
    {
        if (_isPrivateMode || _accountSession is null || !Guid.TryParse(deviceId, out Guid id))
            return;

        try
        {
            if (!await EnsureAal2Async(true))
                return;

            await _supabaseDevices.DenyDeviceAsync(_accountSession, id);
            await RefreshAccountStateAsync("The pending device request was removed.");
        }
        catch (Exception exception)
        {
            await RefreshAccountStateAsync(SafeAccountMessage(exception));
        }

        RefreshSettingsPageIfOpen();
    }

    private async Task SyncNowAsync()
    {
        if (_isPrivateMode || _accountSession is null)
            return;

        try
        {
            if (!await EnsureAal2Async(true))
            {
                await RefreshAccountStateAsync("Verify 2FA before syncing.");
                return;
            }

            await SyncNowInternalAsync(true);
        }
        catch (Exception exception)
        {
            await RefreshAccountStateAsync(SafeAccountMessage(exception));
        }

        RefreshSettingsPageIfOpen();
    }

    private async Task SyncNowInternalAsync(bool refreshUi)
    {
        if (_syncBusy || _accountSession is null || _isPrivateMode || !_store.Settings.SyncEnabled)
            return;

        if (!JwtAssuranceLevel.IsAal2(_accountSession.AccessToken))
            return;

        if (!HasLocalSyncKey(_accountSession.UserId))
            return;

        _syncBusy = true;
        try
        {
            SaveSessionSnapshot();
            SyncRunResult result = await _syncCoordinator.SyncAsync(_accountSession);
            ApplyRuntimeSettings();
            StatusText.Text = result.Uploaded ? "Feather Sync updated" : "Feather Sync is up to date";

            if (refreshUi)
                await RefreshAccountStateAsync(result.Uploaded ? "Encrypted sync uploaded successfully." : "Encrypted sync is already up to date.");
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

        try
        {
            if (!await EnsureAal2Async(true))
                return;

            await _zeroKnowledgeSync.DeleteRemoteAsync(_accountSession);
            _syncCoordinator.DeleteLocalState();
            _store.Settings.SyncEnabled = false;
            _store.SaveSettings();
            ConfigureAutomaticSync();
            await RefreshAccountStateAsync("Encrypted cloud sync data was deleted. Local browsing data was not removed.");
        }
        catch (Exception exception)
        {
            await RefreshAccountStateAsync(SafeAccountMessage(exception));
        }

        RefreshSettingsPageIfOpen();
    }

    private async Task RefreshAccountStateAsync(string message = "")
    {
        if (_isPrivateMode)
        {
            _accountPageState = AccountPageState.SignedOut(true, message);
            return;
        }

        SupabaseSession? session = _accountSession;
        if (session is null)
        {
            _accountPageState = AccountPageState.SignedOut(false, message);
            return;
        }

        string assurance = JwtAssuranceLevel.Read(session.AccessToken);
        List<MfaFactor> factors = [];
        List<RemoteDevice> remoteDevices = [];
        bool remoteExists = false;
        string stateMessage = message;

        try
        {
            factors = await _supabaseMfa.ListFactorsAsync(session);
        }
        catch (Exception exception)
        {
            if (stateMessage.Length == 0)
                stateMessage = SafeAccountMessage(exception);
        }

        bool hasMfa = factors.Any(item => item.IsVerifiedTotp);
        bool aal2 = string.Equals(assurance, "aal2", StringComparison.Ordinal);
        bool hasLocalKey = HasLocalSyncKey(session.UserId);

        if (aal2)
        {
            try
            {
                remoteExists = await _supabaseSync.GetAsync(session) is not null;
                remoteDevices = await _supabaseDevices.ListDevicesAsync(session);
            }
            catch (Exception exception)
            {
                if (stateMessage.Length == 0)
                    stateMessage = SafeAccountMessage(exception);
            }
        }

        Guid? currentDeviceId = null;
        string pairingCode = "";

        try
        {
            using DeviceIdentity? identity = _deviceIdentityStore.Load();
            if (identity is not null)
            {
                currentDeviceId = identity.DeviceId;
                RemoteDevice? current = remoteDevices.FirstOrDefault(device => device.DeviceId == identity.DeviceId);
                if (current is not null && string.Equals(current.Status, "pending", StringComparison.OrdinalIgnoreCase))
                    pairingCode = current.PairingCode;
            }
        }
        catch (SyncSecurityException exception)
        {
            if (stateMessage.Length == 0)
                stateMessage = exception.Message;
        }

        SyncLocalState? localState = _syncCoordinator.GetState(session.UserId);
        string lastSync = localState is null
            ? "Never"
            : localState.LastSyncUtc.ToLocalTime().ToString("g");

        string syncStatus = !hasMfa
            ? "2FA setup required"
            : !aal2
                ? "2FA verification required"
                : !remoteExists
                    ? "Ready to enable encrypted sync"
                    : !hasLocalKey
                        ? pairingCode.Length > 0 ? "Waiting for device approval" : "Recovery or device approval required"
                        : _store.Settings.SyncEnabled ? "Encrypted sync active" : "Encrypted sync available";

        _accountPageState = new AccountPageState
        {
            IsSignedIn = true,
            Email = session.Email,
            UserId = session.UserId,
            AssuranceLevel = assurance,
            HasVerifiedMfa = hasMfa,
            HasLocalSyncKey = hasLocalKey,
            RemoteSyncExists = remoteExists,
            SyncEnabled = _store.Settings.SyncEnabled && hasLocalKey && remoteExists,
            CanApproveDevices = aal2 && hasLocalKey,
            TotpSetupActive = _pendingTotpEnrollment is not null,
            TotpSecret = _pendingTotpEnrollment?.Secret ?? "",
            TotpQrCodeSvg = _pendingTotpEnrollment?.QrCodeSvg ?? "",
            PairingCode = pairingCode,
            SyncStatus = syncStatus,
            LastSync = lastSync,
            Message = stateMessage,
            Devices = remoteDevices.Select(device => new AccountDeviceState
            {
                Id = device.DeviceId.ToString(),
                Name = device.DeviceName,
                Status = device.Status,
                PairingCode = device.PairingCode,
                IsCurrent = currentDeviceId == device.DeviceId
            }).ToList()
        };
    }

    private bool HasLocalSyncKey(string userId)
    {
        try
        {
            using SyncDeviceSecrets? secrets = _syncDeviceSecretStore.Load();
            return secrets is not null &&
                   string.Equals(secrets.UserId, userId, StringComparison.OrdinalIgnoreCase);
        }
        catch (SyncSecurityException)
        {
            return false;
        }
    }

    private bool CanRunAutomaticSync() =>
        !_isPrivateMode &&
        _accountSession is not null &&
        JwtAssuranceLevel.IsAal2(_accountSession.AccessToken) &&
        _store.Settings.SyncEnabled &&
        _store.Settings.AutoSyncEnabled &&
        HasLocalSyncKey(_accountSession.UserId);

    private void ConfigureAutomaticSync()
    {
        _syncTimer.Stop();
        _syncTimer.Interval = TimeSpan.FromMinutes(Math.Clamp(_store.Settings.AutoSyncMinutes, 1, 60));

        if (CanRunAutomaticSync())
            _syncTimer.Start();
    }

    private async Task AutomaticSyncTickAsync()
    {
        if (_isPrivateMode || _accountSession is null || !_store.Settings.SyncEnabled || !_store.Settings.AutoSyncEnabled)
        {
            ConfigureAutomaticSync();
            return;
        }

        try
        {
            if (_accountSession.NeedsRefresh)
            {
                SupabaseSession old = _accountSession;
                SupabaseSession refreshed = await _supabaseAuth.RefreshAsync(old.RefreshToken);
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

            if (!CanRunAutomaticSync())
            {
                ConfigureAutomaticSync();
                return;
            }

            await SyncNowInternalAsync(false);
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Feather Sync: {SafeAccountMessage(exception)}";
        }
    }

    private void RefreshSettingsPageIfOpen()
    {
        if (_activeTab?.IsSettingsPage == true)
            ShowSettingsPage(_activeTab);
    }

    private static string SafeAccountMessage(Exception exception) =>
        exception switch
        {
            SyncSecurityException => exception.Message,
            MfaRequiredException => exception.Message,
            HttpRequestException => exception.Message,
            InvalidDataException => exception.Message,
            InvalidOperationException => exception.Message,
            ArgumentException => exception.Message,
            _ => "Feather Account could not complete that request."
        };
}
