using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace FeatherBrowser.Infrastructure.Supabase;

internal sealed class SupabaseAuthClient : IDisposable
{
    private readonly SupabaseConfig _config;
    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;

    public SupabaseAuthClient(
        SupabaseConfig config,
        HttpClient? httpClient = null)
    {
        _config = config;
        _ownsHttpClient = httpClient is null;
        _http = httpClient ?? new HttpClient();
        _http.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<SupabaseAuthResult> SignUpAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        ValidateCredentials(email, password);

        using HttpRequestMessage request = CreateRequest(
            HttpMethod.Post,
            "auth/v1/signup");

        request.Content = JsonContent.Create(new
        {
            email = email.Trim(),
            password
        });

        using HttpResponseMessage response =
            await _http.SendAsync(request, cancellationToken);

        string json = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw CreateSafeAuthException(response, json);

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        string? userId = TryReadUserId(root);
        SupabaseSession? session = TryReadSession(root, userId);

        return new SupabaseAuthResult
        {
            UserId = userId,
            Session = session,
            RequiresEmailConfirmation = session is null
        };
    }

    public async Task<SupabaseSession> SignInAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        ValidateCredentials(email, password);

        using HttpRequestMessage request = CreateRequest(
            HttpMethod.Post,
            "auth/v1/token?grant_type=password");

        request.Content = JsonContent.Create(new
        {
            email = email.Trim(),
            password
        });

        using HttpResponseMessage response =
            await _http.SendAsync(request, cancellationToken);

        string json = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw CreateSafeAuthException(response, json);

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        string? userId = TryReadUserId(root);

        return TryReadSession(root, userId)
               ?? throw new InvalidOperationException(
                   "Supabase authenticated the account but did not return a usable session.");
    }

    public async Task<SupabaseSession> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            throw new ArgumentException("A refresh token is required.", nameof(refreshToken));

        using HttpRequestMessage request = CreateRequest(
            HttpMethod.Post,
            "auth/v1/token?grant_type=refresh_token");

        request.Content = JsonContent.Create(new
        {
            refresh_token = refreshToken
        });

        using HttpResponseMessage response =
            await _http.SendAsync(request, cancellationToken);

        string json = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw CreateSafeAuthException(response, json);

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        string? userId = TryReadUserId(root);

        return TryReadSession(root, userId)
               ?? throw new InvalidOperationException(
                   "Supabase did not return a usable refreshed session.");
    }

    public async Task SignOutAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
            return;

        using HttpRequestMessage request = CreateRequest(
            HttpMethod.Post,
            "auth/v1/logout");

        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                accessToken);

        using HttpResponseMessage response =
            await _http.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            string json =
                await response.Content.ReadAsStringAsync(cancellationToken);

            throw CreateSafeAuthException(response, json);
        }
    }

    private HttpRequestMessage CreateRequest(
        HttpMethod method,
        string relativePath)
    {
        var request = new HttpRequestMessage(
            method,
            new Uri(_config.BaseUri, relativePath));

        request.Headers.TryAddWithoutValidation(
            "apikey",
            _config.PublishableKey);

        request.Headers.Accept.ParseAdd("application/json");

        return request;
    }

    private static SupabaseSession? TryReadSession(
        JsonElement root,
        string? userId)
    {
        if (string.IsNullOrWhiteSpace(userId) ||
            !root.TryGetProperty("access_token", out JsonElement accessTokenElement) ||
            accessTokenElement.ValueKind != JsonValueKind.String ||
            !root.TryGetProperty("refresh_token", out JsonElement refreshTokenElement) ||
            refreshTokenElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        string accessToken = accessTokenElement.GetString() ?? "";
        string refreshToken = refreshTokenElement.GetString() ?? "";

        if (accessToken.Length == 0 || refreshToken.Length == 0)
            return null;

        long expiresIn = 3600;

        if (root.TryGetProperty("expires_in", out JsonElement expiresElement) &&
            expiresElement.TryGetInt64(out long parsedExpires) &&
            parsedExpires > 0)
        {
            expiresIn = parsedExpires;
        }

        return new SupabaseSession
        {
            UserId = userId,
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            Email = TryReadEmail(root),
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn)
        };
    }

    private static string TryReadEmail(JsonElement root)
    {
        if (!root.TryGetProperty("user", out JsonElement user) ||
            user.ValueKind != JsonValueKind.Object ||
            !user.TryGetProperty("email", out JsonElement email) ||
            email.ValueKind != JsonValueKind.String)
        {
            return "";
        }

        return email.GetString() ?? "";
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

    private static Exception CreateSafeAuthException(
        HttpResponseMessage response,
        string json)
    {
        string message = "Supabase authentication failed.";

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

        return new HttpRequestException(
            $"{message} HTTP {(int)response.StatusCode}.");
    }

    private static void ValidateCredentials(
        string email,
        string password)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email is required.", nameof(email));

        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Password is required.", nameof(password));
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
            _http.Dispose();
    }
}
