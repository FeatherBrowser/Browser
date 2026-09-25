using System.Text;
using System.Text.Json;

namespace FeatherBrowser.Infrastructure.Supabase;

internal static class JwtAssuranceLevel
{
    public static bool IsAal2(string jwt) =>
        string.Equals(
            Read(jwt),
            "aal2",
            StringComparison.Ordinal);

    public static string Read(string jwt)
    {
        if (string.IsNullOrWhiteSpace(jwt))
            return "aal1";

        string[] parts = jwt.Split('.');

        if (parts.Length != 3)
            return "aal1";

        try
        {
            string payload = parts[1]
                .Replace('-', '+')
                .Replace('_', '/');

            payload = payload.PadRight(
                payload.Length + ((4 - payload.Length % 4) % 4),
                '=');

            byte[] bytes = Convert.FromBase64String(payload);

            try
            {
                using JsonDocument document =
                    JsonDocument.Parse(bytes);

                if (document.RootElement.TryGetProperty(
                        "aal",
                        out JsonElement aal) &&
                    aal.ValueKind == JsonValueKind.String)
                {
                    return aal.GetString() ?? "aal1";
                }

                return "aal1";
            }
            finally
            {
                Array.Clear(bytes);
            }
        }
        catch (FormatException)
        {
            return "aal1";
        }
        catch (JsonException)
        {
            return "aal1";
        }
    }
}
