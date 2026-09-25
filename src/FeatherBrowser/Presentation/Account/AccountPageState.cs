namespace FeatherBrowser.Presentation.Account;

internal sealed class AccountPageState
{
    public bool IsPrivateMode { get; init; }
    public bool IsSignedIn { get; init; }
    public string Email { get; init; } = "";
    public string UserId { get; init; } = "";
    public string AssuranceLevel { get; init; } = "aal1";
    public bool HasVerifiedMfa { get; init; }
    public bool HasLocalSyncKey { get; init; }
    public bool RemoteSyncExists { get; init; }
    public bool SyncEnabled { get; init; }
    public bool CanApproveDevices { get; init; }
    public bool TotpSetupActive { get; init; }
    public string TotpSecret { get; init; } = "";
    public string TotpQrCodeSvg { get; init; } = "";
    public string PairingCode { get; init; } = "";
    public string SyncStatus { get; init; } = "Not signed in";
    public string LastSync { get; init; } = "Never";
    public string Message { get; init; } = "";
    public List<AccountDeviceState> Devices { get; init; } = [];

    public static AccountPageState SignedOut(bool privateMode, string message = "") =>
        new()
        {
            IsPrivateMode = privateMode,
            SyncStatus = privateMode ? "Unavailable in private windows" : "Not signed in",
            Message = message
        };
}

internal sealed class AccountDeviceState
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Status { get; init; } = "";
    public string PairingCode { get; init; } = "";
    public bool IsCurrent { get; init; }
}
