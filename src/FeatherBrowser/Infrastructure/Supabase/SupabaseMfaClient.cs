using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace FeatherBrowser.Infrastructure.Supabase;

internal sealed class SupabaseMfaClient : IDisposable
{
    private readonly SupabaseConfig _config;
    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;

    public SupabaseMfaClient(
        SupabaseConfig config,
        HttpClient? httpClient = null)
    {
        _config = config;
        _ownsHttpClient = httpClient is null;
        _http = httpClient ?? new HttpClient();
        _http.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<TotpEnrollment> EnrollTotpAsync(
        SupabaseSession session,
        string friendlyName,
        CancellationToken cancellationToken = default)
    {
        ValidateSession(session);

        using HttpRequestMessage request = CreateAuthorizedRequest(
            HttpMethod.Post,
            "auth/v1/factors",
            session.AccessToken);

        request.Content = JsonContent.Create(new
        {
            factor_type = "totp",
            friendly_name = string.IsNullOrWhiteSpace(friendlyName)
                ? "Feather Browser"
                : friendlyName.Trim()
        });

        using HttpResponseMessage response =
            await _http.SendAsync(request, cancellationToken);

        string json =
            await response.Content.ReadAsStringAsync(cancellationToken);

        EnsureSuccess(response, json);

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        string factorId =
            root.GetProperty("id").GetString()
            ?? throw new InvalidDataException("Supabase did not return an MFA factor id.");

        JsonElement totp = root.GetProperty("totp");

        return new TotpEnrollment
        {
            FactorId = factorId,
            FriendlyName = root.TryGetProperty("friendly_name", out JsonElement name)
                ? name.GetString() ?? friendlyName
                : friendlyName,
            Secret = totp.TryGetProperty("secret", out JsonElement secret)
                ? secret.GetString() ?? ""
                : "",
            Uri = totp.TryGetProperty("uri", out JsonElement uri)
                ? uri.GetString() ?? ""
                : "",
            QrCodeSvg = totp.TryGetProperty("qr_code", out JsonElement qrCode)
                ? qrCode.GetString() ?? ""
                : ""
        };
    }

    public async Task<List<MfaFactor>> ListFactorsAsync(
        SupabaseSession session,
        CancellationToken cancellationToken = default)
    {
        ValidateSession(session);

        using HttpRequestMessage request = CreateAuthorizedRequest(
            HttpMethod.Get,
            "auth/v1/user",
            session.AccessToken);

        using HttpResponseMessage response =
            await _http.SendAsync(request, cancellationToken);

        string json =
            await response.Content.ReadAsStringAsync(cancellationToken);

        EnsureSuccess(response, json);

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        if (!root.TryGetProperty("factors", out JsonElement factors) ||
            factors.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<MfaFactor>();

        foreach (JsonElement factor in factors.EnumerateArray())
        {
            string id = factor.TryGetProperty("id", out JsonElement idElement)
                ? idElement.GetString() ?? ""
                : "";

            if (!Guid.TryParse(id, out _))
                continue;

            result.Add(new MfaFactor
            {
                Id = id,
                Type = factor.TryGetProperty("factor_type", out JsonElement type)
                    ? type.GetString() ?? ""
                    : "",
                Status = factor.TryGetProperty("status", out JsonElement status)
                    ? status.GetString() ?? ""
                    : "",
                FriendlyName = factor.TryGetProperty("friendly_name", out JsonElement friendlyName)
                    ? friendlyName.GetString() ?? ""
                    : ""
            });
        }

        return result;
    }

    public async Task<MfaChallenge> ChallengeAsync(
        SupabaseSession session,
        string factorId,
        CancellationToken cancellationToken = default)
    {
        ValidateSession(session);
        ValidateFactorId(factorId);

        using HttpRequestMessage request = CreateAuthorizedRequest(
            HttpMethod.Post,
            $"auth/v1/factors/{Uri.EscapeDataString(factorId)}/challenge",
            session.AccessToken);

        request.Content = JsonContent.Create(new { });

        using HttpResponseMessage response =
            await _http.SendAsync(request, cancellationToken);

        string json =
            await response.Content.ReadAsStringAsync(cancellationToken);

        EnsureSuccess(response, json);

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        return new MfaChallenge
        {
            ChallengeId =
                root.GetProperty("id").GetString()
                ?? throw new InvalidDataException(
                    "Supabase did not return an MFA challenge id.")
        };
    }

    public async Task<SupabaseSession> VerifyAsync(
        SupabaseSession aal1Session,
        string factorId,
        string challengeId,
        string code,
        CancellationToken cancellationToken = default)
    {
        ValidateSession(aal1Session);
        ValidateFactorId(factorId);

        if (string.IsNullOrWhiteSpace(challengeId))
            throw new ArgumentException("An MFA challenge id is required.", nameof(challengeId));

        string normalizedCode = new(
            (code ?? string.Empty)
                .Where(char.IsDigit)
                .ToArray());

        if (normalizedCode.Length != 6)
            throw new ArgumentException("A six-digit authenticator code is required.", nameof(code));

        using HttpRequestMessage request = CreateAuthorizedRequest(
            HttpMethod.Post,
            $"auth/v1/factors/{Uri.EscapeDataString(factorId)}/verify",
            aal1Session.AccessToken);

        request.Content = JsonContent.Create(new
        {
            factor_id = factorId,
            challenge_id = challengeId,
            code = normalizedCode
        });

        using HttpResponseMessage response =
            await _http.SendAsync(request, cancellationToken);

        string json =
            await response.Content.ReadAsStringAsync(cancellationToken);

        EnsureSuccess(response, json);

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        string accessToken =
            root.GetProperty("access_token").GetString()
            ?? throw new InvalidDataException(
                "Supabase did not return an AAL2 access token.");

        string refreshToken =
            root.GetProperty("refresh_token").GetString()
            ?? throw new InvalidDataException(
                "Supabase did not return an AAL2 refresh token.");

        string userId = TryReadUserId(root)
            ?? aal1Session.UserId;

        long expiresIn = 3600;

        if (root.TryGetProperty("expires_in", out JsonElement expires) &&
            expires.TryGetInt64(out long parsed) &&
            parsed > 0)
        {
            expiresIn = parsed;
        }

        var session = new SupabaseSession
        {
            UserId = userId,
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            Email = TryReadEmail(root) ?? aal1Session.Email,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn)
        };

        if (!JwtAssuranceLevel.IsAal2(accessToken))
        {
            throw new InvalidDataException(
                "Supabase verified MFA but the returned token is not AAL2.");
        }

        return session;
    }

    public async Task<SupabaseSession> ChallengeAndVerifyAsync(
        SupabaseSession aal1Session,
        string factorId,
        string code,
        CancellationToken cancellationToken = default)
    {
        MfaChallenge challenge =
            await ChallengeAsync(
                aal1Session,
                factorId,
                cancellationToken);

        return await VerifyAsync(
            aal1Session,
            factorId,
            challenge.ChallengeId,
            code,
            cancellationToken);
    }

    public async Task UnenrollAsync(
        SupabaseSession aal2Session,
        string factorId,
        CancellationToken cancellationToken = default)
    {
        RequireAal2(aal2Session);
        ValidateFactorId(factorId);

        using HttpRequestMessage request = CreateAuthorizedRequest(
            HttpMethod.Delete,
            $"auth/v1/factors/{Uri.EscapeDataString(factorId)}",
            aal2Session.AccessToken);

        using HttpResponseMessage response =
            await _http.SendAsync(request, cancellationToken);

        string json =
            await response.Content.ReadAsStringAsync(cancellationToken);

        EnsureSuccess(response, json);
    }

    private HttpRequestMessage CreateAuthorizedRequest(
        HttpMethod method,
        string path,
        string accessToken)
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
                accessToken);

        request.Headers.Accept.ParseAdd("application/json");

        return request;
    }

    private static string? TryReadUserId(JsonElement root)
    {
        if (!root.TryGetProperty("user", out JsonElement user) ||
            user.ValueKind != JsonValueKind.Object ||
            !user.TryGetProperty("id", out JsonElement id) ||
            id.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        string? value = id.GetString();

        return Guid.TryParse(value, out _)
            ? value
            : null;
    }

    private static string? TryReadEmail(JsonElement root)
    {
        if (!root.TryGetProperty("user", out JsonElement user) ||
            user.ValueKind != JsonValueKind.Object ||
            !user.TryGetProperty("email", out JsonElement email) ||
            email.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return email.GetString();
    }

    private static void ValidateSession(SupabaseSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (!Guid.TryParse(session.UserId, out _))
            throw new InvalidOperationException(
                "The Supabase session has an invalid user id.");

        if (string.IsNullOrWhiteSpace(session.AccessToken))
            throw new InvalidOperationException(
                "The Supabase session has no access token.");
    }

    public static void RequireAal2(SupabaseSession session)
    {
        ValidateSession(session);

        if (!JwtAssuranceLevel.IsAal2(session.AccessToken))
        {
            throw new MfaRequiredException(
                "Feather Sync requires multi-factor authentication for this action.");
        }
    }

    private static void ValidateFactorId(string factorId)
    {
        if (!Guid.TryParse(factorId, out _))
            throw new ArgumentException(
                "The MFA factor id is invalid.",
                nameof(factorId));
    }

    private static void EnsureSuccess(
        HttpResponseMessage response,
        string json)
    {
        if (response.IsSuccessStatusCode)
            return;

        string message = "Supabase MFA request failed.";

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            foreach (string property in new[]
                     {
                         "msg",
                         "message",
                         "error_description",
                         "error"
                     })
            {
                if (root.TryGetProperty(property, out JsonElement value) &&
                    value.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(value.GetString()))
                {
                    message = value.GetString()!;
                    break;
                }
            }
        }
        catch (JsonException)
        {
        }

        throw new HttpRequestException(
            $"{message} HTTP {(int)response.StatusCode}.");
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
            _http.Dispose();
    }
}
