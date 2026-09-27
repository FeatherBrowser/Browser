using System.Text.Json;

namespace FeatherBrowser.Features.Security;

internal static class InternalPageMessagePolicy
{
    private const string InternalDocumentSource = "about:blank";

    public static bool IsInternalDocumentSource(string? source) =>
        string.Equals(source, InternalDocumentSource, StringComparison.OrdinalIgnoreCase);

    public static bool IsTrusted(
        bool isInternalPage,
        string? expectedToken,
        string? currentSource,
        string? messageSource,
        string? messageJson)
    {
        if (!isInternalPage ||
            string.IsNullOrWhiteSpace(expectedToken) ||
            !IsInternalDocumentSource(currentSource) ||
            !IsInternalDocumentSource(messageSource) ||
            string.IsNullOrWhiteSpace(messageJson))
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(messageJson);
            JsonElement root = document.RootElement;

            return root.ValueKind == JsonValueKind.Object &&
                   root.TryGetProperty("__featherToken", out JsonElement tokenElement) &&
                   tokenElement.ValueKind == JsonValueKind.String &&
                   string.Equals(
                       tokenElement.GetString(),
                       expectedToken,
                       StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
