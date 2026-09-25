using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FeatherBrowser.Features.Sync.Models;

namespace FeatherBrowser.Infrastructure.Supabase;

internal sealed class SupabaseSyncClient : IDisposable
{
    private readonly SupabaseConfig _config;
    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;

    public SupabaseSyncClient(
        SupabaseConfig config,
        HttpClient? httpClient = null)
    {
        _config = config;
        _ownsHttpClient = httpClient is null;
        _http = httpClient ?? new HttpClient();
        _http.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<RemoteSyncRecord?> GetAsync(
        SupabaseSession session,
        CancellationToken cancellationToken = default)
    {
        ValidateSession(session);

        string userId = Uri.EscapeDataString(session.UserId);
        string path =
            $"rest/v1/feather_sync?select=user_id,format_version,blob_revision,key_bundle,encrypted_blob,updated_at&user_id=eq.{userId}&limit=1";

        using HttpRequestMessage request =
            CreateAuthorizedRequest(HttpMethod.Get, path, session);

        using HttpResponseMessage response =
            await _http.SendAsync(request, cancellationToken);

        string json =
            await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw CreateSafeSyncException(response);

        using JsonDocument document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind != JsonValueKind.Array ||
            document.RootElement.GetArrayLength() == 0)
        {
            return null;
        }

        JsonElement row = document.RootElement[0];

        SyncKeyBundle? keyBundle =
            row.GetProperty("key_bundle")
                .Deserialize<SyncKeyBundle>();

        EncryptedSyncBlob? encryptedBlob =
            row.GetProperty("encrypted_blob")
                .Deserialize<EncryptedSyncBlob>();

        if (keyBundle is null || encryptedBlob is null)
            throw new InvalidDataException("The remote Feather sync record is incomplete.");

        return new RemoteSyncRecord
        {
            UserId = row.GetProperty("user_id").GetString() ?? "",
            FormatVersion = row.GetProperty("format_version").GetInt32(),
            BlobRevision = row.GetProperty("blob_revision").GetInt64(),
            KeyBundle = keyBundle,
            EncryptedBlob = encryptedBlob,
            UpdatedAt = row.GetProperty("updated_at").GetDateTimeOffset()
        };
    }

    public async Task<long> CommitAsync(
        SupabaseSession session,
        long expectedRevision,
        SyncKeyBundle keyBundle,
        EncryptedSyncBlob encryptedBlob,
        CancellationToken cancellationToken = default)
    {
        ValidateSession(session);
        ArgumentNullException.ThrowIfNull(keyBundle);
        ArgumentNullException.ThrowIfNull(encryptedBlob);

        if (expectedRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(expectedRevision));

        using HttpRequestMessage request =
            CreateAuthorizedRequest(
                HttpMethod.Post,
                "rest/v1/rpc/feather_commit_sync",
                session);

        request.Content = JsonContent.Create(new
        {
            p_expected_revision = expectedRevision,
            p_format_version = 1,
            p_key_bundle = keyBundle,
            p_encrypted_blob = encryptedBlob
        });

        using HttpResponseMessage response =
            await _http.SendAsync(request, cancellationToken);

        string json =
            await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            if ((int)response.StatusCode == 409 ||
                json.Contains("40001", StringComparison.Ordinal))
            {
                throw new SyncRevisionConflictException();
            }

            throw CreateSafeSyncException(response);
        }

        using JsonDocument document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetInt64(out long revision) ||
            revision <= 0)
        {
            throw new InvalidDataException(
                "Supabase returned an invalid Feather sync revision.");
        }

        return revision;
    }

    public async Task DeleteAsync(
        SupabaseSession session,
        CancellationToken cancellationToken = default)
    {
        ValidateSession(session);

        using HttpRequestMessage request =
            CreateAuthorizedRequest(
                HttpMethod.Post,
                "rest/v1/rpc/feather_delete_sync",
                session);

        request.Content = JsonContent.Create(new { });

        using HttpResponseMessage response =
            await _http.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw CreateSafeSyncException(response);
    }

    private HttpRequestMessage CreateAuthorizedRequest(
        HttpMethod method,
        string relativePath,
        SupabaseSession session)
    {
        var request = new HttpRequestMessage(
            method,
            new Uri(_config.BaseUri, relativePath));

        request.Headers.TryAddWithoutValidation(
            "apikey",
            _config.PublishableKey);

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                session.AccessToken);

        request.Headers.Accept.ParseAdd("application/json");

        return request;
    }

    private static void ValidateSession(SupabaseSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (!Guid.TryParse(session.UserId, out _))
            throw new InvalidOperationException("The Supabase session has an invalid user id.");

        if (string.IsNullOrWhiteSpace(session.AccessToken))
            throw new InvalidOperationException("The Supabase session has no access token.");
    }

    private static Exception CreateSafeSyncException(
        HttpResponseMessage response) =>
        new HttpRequestException(
            $"Feather Sync request failed with HTTP {(int)response.StatusCode}.");

    public void Dispose()
    {
        if (_ownsHttpClient)
            _http.Dispose();
    }
}

internal sealed class SyncRevisionConflictException : Exception
{
    public SyncRevisionConflictException()
        : base("The remote Feather sync record changed on another device. Pull and merge before trying again.")
    {
    }
}
