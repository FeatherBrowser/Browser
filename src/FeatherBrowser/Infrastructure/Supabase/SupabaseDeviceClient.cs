using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FeatherBrowser.Features.Sync.Devices;

namespace FeatherBrowser.Infrastructure.Supabase;

internal sealed class SupabaseDeviceClient : IDisposable
{
    private readonly SupabaseConfig _config;
    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;

    public SupabaseDeviceClient(
        SupabaseConfig config,
        HttpClient? httpClient = null)
    {
        _config = config;
        _ownsHttpClient = httpClient is null;
        _http = httpClient ?? new HttpClient();
        _http.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task RegisterFirstDeviceAsync(
        SupabaseSession session,
        DeviceIdentity identity,
        CancellationToken cancellationToken = default)
    {
        SupabaseMfaClient.RequireAal2(session);

        await PostRpcAsync(
            session,
            "feather_register_first_device",
            new
            {
                p_device_id = identity.DeviceId,
                p_device_name = identity.DeviceName,
                p_public_key = identity.PublicKey
            },
            cancellationToken);
    }

    public async Task RegisterPendingDeviceAsync(
        SupabaseSession session,
        DeviceIdentity identity,
        CancellationToken cancellationToken = default)
    {
        SupabaseMfaClient.RequireAal2(session);

        await PostRpcAsync(
            session,
            "feather_register_pending_device",
            new
            {
                p_device_id = identity.DeviceId,
                p_device_name = identity.DeviceName,
                p_public_key = identity.PublicKey
            },
            cancellationToken);
    }

    public async Task<List<RemoteDevice>> ListDevicesAsync(
        SupabaseSession session,
        CancellationToken cancellationToken = default)
    {
        SupabaseMfaClient.RequireAal2(session);

        using HttpRequestMessage request = CreateAuthorizedRequest(
            HttpMethod.Get,
            "rest/v1/feather_devices?select=device_id,device_name,public_key,status,created_at,last_seen_at&order=created_at.asc",
            session);

        using HttpResponseMessage response =
            await _http.SendAsync(request, cancellationToken);

        string json =
            await response.Content.ReadAsStringAsync(cancellationToken);

        EnsureSuccess(response);

        using JsonDocument document =
            JsonDocument.Parse(json);

        var devices =
            new List<RemoteDevice>();

        foreach (JsonElement row in
                 document.RootElement.EnumerateArray())
        {
            string deviceId =
                row.GetProperty("device_id").GetString() ?? "";

            if (!Guid.TryParse(
                    deviceId,
                    out Guid parsedId))
            {
                continue;
            }

            devices.Add(
                new RemoteDevice
                {
                    DeviceId = parsedId,
                    DeviceName =
                        row.GetProperty("device_name").GetString()
                        ?? "Feather device",
                    PublicKey =
                        row.GetProperty("public_key").GetString()
                        ?? "",
                    Status =
                        row.GetProperty("status").GetString()
                        ?? "pending",
                    CreatedAt =
                        row.GetProperty("created_at")
                            .GetDateTimeOffset(),
                    LastSeenAt =
                        row.GetProperty("last_seen_at")
                            .GetDateTimeOffset()
                });
        }

        return devices;
    }

    public async Task SubmitTransferAsync(
        SupabaseSession session,
        DeviceKeyTransfer transfer,
        CancellationToken cancellationToken = default)
    {
        SupabaseMfaClient.RequireAal2(session);

        await PostRpcAsync(
            session,
            "feather_submit_device_transfer",
            new
            {
                p_source_device_id =
                    Guid.Parse(transfer.SourceDeviceId),
                p_target_device_id =
                    Guid.Parse(transfer.TargetDeviceId),
                p_source_public_key =
                    transfer.SourcePublicKey,
                p_key_bundle_fingerprint =
                    transfer.KeyBundleFingerprint,
                p_salt =
                    transfer.Salt,
                p_nonce =
                    transfer.Nonce,
                p_tag =
                    transfer.Tag,
                p_encrypted_master_key =
                    transfer.EncryptedMasterKey
            },
            cancellationToken);
    }

    public async Task RegisterRecoveredDeviceAsync(
        SupabaseSession session,
        DeviceIdentity identity,
        CancellationToken cancellationToken = default)
    {
        SupabaseMfaClient.RequireAal2(session);

        await PostRpcAsync(
            session,
            "feather_register_recovered_device",
            new
            {
                p_device_id = identity.DeviceId,
                p_device_name = identity.DeviceName,
                p_public_key = identity.PublicKey
            },
            cancellationToken);
    }

    public async Task DenyDeviceAsync(
        SupabaseSession session,
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        SupabaseMfaClient.RequireAal2(session);

        await PostRpcAsync(
            session,
            "feather_deny_device",
            new
            {
                p_device_id = deviceId
            },
            cancellationToken);
    }

    public async Task<DeviceKeyTransfer?> GetTransferAsync(
        SupabaseSession session,
        Guid targetDeviceId,
        CancellationToken cancellationToken = default)
    {
        SupabaseMfaClient.RequireAal2(session);

        string target =
            Uri.EscapeDataString(
                targetDeviceId.ToString());

        string path =
            $"rest/v1/feather_device_transfers?select=source_device_id,target_device_id,source_public_key,key_bundle_fingerprint,salt,nonce,tag,encrypted_master_key&target_device_id=eq.{target}&limit=1";

        using HttpRequestMessage request =
            CreateAuthorizedRequest(
                HttpMethod.Get,
                path,
                session);

        using HttpResponseMessage response =
            await _http.SendAsync(
                request,
                cancellationToken);

        string json =
            await response.Content.ReadAsStringAsync(
                cancellationToken);

        EnsureSuccess(response);

        using JsonDocument document =
            JsonDocument.Parse(json);

        if (document.RootElement.GetArrayLength() == 0)
            return null;

        JsonElement row =
            document.RootElement[0];

        return new DeviceKeyTransfer
        {
            SourceDeviceId =
                row.GetProperty("source_device_id")
                    .GetString() ?? "",
            TargetDeviceId =
                row.GetProperty("target_device_id")
                    .GetString() ?? "",
            SourcePublicKey =
                row.GetProperty("source_public_key")
                    .GetString() ?? "",
            KeyBundleFingerprint =
                row.GetProperty("key_bundle_fingerprint")
                    .GetString() ?? "",
            Salt =
                row.GetProperty("salt")
                    .GetString() ?? "",
            Nonce =
                row.GetProperty("nonce")
                    .GetString() ?? "",
            Tag =
                row.GetProperty("tag")
                    .GetString() ?? "",
            EncryptedMasterKey =
                row.GetProperty("encrypted_master_key")
                    .GetString() ?? ""
        };
    }

    public async Task ConsumeTransferAsync(
        SupabaseSession session,
        Guid targetDeviceId,
        CancellationToken cancellationToken = default)
    {
        SupabaseMfaClient.RequireAal2(session);

        await PostRpcAsync(
            session,
            "feather_consume_device_transfer",
            new
            {
                p_target_device_id =
                    targetDeviceId
            },
            cancellationToken);
    }

    private async Task PostRpcAsync(
        SupabaseSession session,
        string rpc,
        object body,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request =
            CreateAuthorizedRequest(
                HttpMethod.Post,
                $"rest/v1/rpc/{rpc}",
                session);

        request.Content =
            JsonContent.Create(body);

        using HttpResponseMessage response =
            await _http.SendAsync(
                request,
                cancellationToken);

        EnsureSuccess(response);
    }

    private HttpRequestMessage CreateAuthorizedRequest(
        HttpMethod method,
        string path,
        SupabaseSession session)
    {
        var request = new HttpRequestMessage(
            method,
            new Uri(_config.BaseUri, path));

        request.Headers.TryAddWithoutValidation(
            "apikey",
            _config.PublishableKey);

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                session.AccessToken);

        request.Headers.Accept.ParseAdd(
            "application/json");

        return request;
    }

    private static void EnsureSuccess(
        HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;

        if ((int)response.StatusCode is 401 or 403)
        {
            throw new MfaRequiredException(
                "Supabase rejected this sync action. Complete Feather MFA and try again.");
        }

        throw new HttpRequestException(
            $"Feather device request failed with HTTP {(int)response.StatusCode}.");
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
            _http.Dispose();
    }
}
