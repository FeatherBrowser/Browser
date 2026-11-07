using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Text;
using CefSharp;
using FeatherBrowser.Infrastructure.Resources;

namespace FeatherBrowser.Features.Engine;
internal static class InternalPages
{
    public static readonly IReadOnlyList<string> Kinds =
        ["newtab", "settings", "welcome", "history", "favorites", "downloads"];

    private static readonly ConcurrentDictionary<string, Entry> Pages = new(StringComparer.Ordinal);
    private static readonly SchemeFactory Factory = new();

    private static readonly IReadOnlyDictionary<string, string> Assets =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private sealed record Entry(string Kind, string Html, int BrowserId);

    internal const string BridgeScript = """
        (() => {
          if (window.chrome && window.chrome.webview && window.chrome.webview.__feather) return;
          const listeners = new Set();
          const webview = Object.freeze({
            __feather: true,
            postMessage(value) {
              try { CefSharp.PostMessage(JSON.stringify(value)); }
              catch (error) { console.warn("Feather bridge unavailable", error); }
            },
            addEventListener(type, listener) {
              if (type === "message" && typeof listener === "function") listeners.add(listener);
            },
            removeEventListener(type, listener) {
              if (type === "message") listeners.delete(listener);
            }
          });
          window.chrome = window.chrome || {};
          Object.defineProperty(window.chrome, "webview", { value: webview, configurable: false });
          Object.defineProperty(window, "__featherReceive", {
            configurable: false,
            value(data) {
              for (const listener of listeners) {
                try { listener({ data }); } catch (error) { console.error(error); }
              }
            }
          });
        })();
        """;

    public static void Register(IRequestContext context) =>
        context.RegisterSchemeHandlerFactory("https", FeatherEngine.InternalHost, Factory);

    public static string Publish(int browserId, string kind, string html, string? previousNonce)
    {
        if (!Kinds.Contains(kind, StringComparer.Ordinal))
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown internal page.");

        Revoke(previousNonce);
        string nonce = Guid.NewGuid().ToString("N");
        Pages[nonce] = new Entry(kind, html, browserId);
        return $"{FeatherEngine.InternalOrigin}/{kind}/{nonce}";
    }

    public static void Revoke(string? nonce)
    {
        if (!string.IsNullOrEmpty(nonce))
            Pages.TryRemove(nonce, out _);
    }

    public static bool IsInternalUrl(string? url) =>
        url is not null &&
        url.StartsWith(FeatherEngine.InternalOrigin + "/", StringComparison.OrdinalIgnoreCase);

    public static bool TryParsePageUrl(string? url, out string kind, out string nonce)
    {
        kind = string.Empty;
        nonce = string.Empty;

        if (!IsInternalUrl(url) || !Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
            return false;

        string[] segments = uri.AbsolutePath.Trim('/').Split('/');
        if (segments.Length != 2 || !Kinds.Contains(segments[0], StringComparer.Ordinal) || segments[1].Length != 32)
            return false;

        kind = segments[0];
        nonce = segments[1];
        return true;
    }
    public static string FeatherAddressFor(string kind) => $"feather://{kind}";

    internal static string InjectBridge(string html)
    {
        string tag = "<script>" + BridgeScript + "</script>";
        int head = html.IndexOf("<head>", StringComparison.OrdinalIgnoreCase);
        return head >= 0
            ? html.Insert(head + "<head>".Length, tag)
            : tag + html;
    }

    private sealed class SchemeFactory : ISchemeHandlerFactory
    {
        public IResourceHandler Create(IBrowser browser, IFrame frame, string schemeName, IRequest request)
        {
            if (!Uri.TryCreate(request.Url, UriKind.Absolute, out Uri? uri))
                return NotFound();

            string path = uri.AbsolutePath.Trim('/');

            if (path.StartsWith("assets/", StringComparison.OrdinalIgnoreCase))
            {
                string name = path["assets/".Length..];
                if (!Assets.TryGetValue(name, out string? mime))
                    return NotFound();

                var asset = new ResourceHandler(mime, new MemoryStream(EmbeddedAssets.LoadBytes(name), writable: false), autoDisposeStream: true);
                asset.Headers.Add("Cache-Control", "max-age=86400");
                return asset;
            }

            if (frame is null || !frame.IsMain ||
                !TryParsePageUrl(request.Url, out _, out string nonce) ||
                !Pages.TryGetValue(nonce, out Entry? entry) ||
                entry.BrowserId != browser.Identifier)
            {
                return NotFound();
            }

            var page = new ResourceHandler(
                "text/html",
                new MemoryStream(Encoding.UTF8.GetBytes(InjectBridge(entry.Html)), writable: false),
                autoDisposeStream: true,
                charset: "utf-8");
            page.Headers.Add("Cache-Control", "no-store");
            page.Headers.Add("X-Frame-Options", "DENY");
            page.Headers.Add("Content-Security-Policy", "frame-ancestors 'none'");
            page.Headers.Add("Referrer-Policy", "no-referrer");
            return page;
        }

        private static IResourceHandler NotFound() =>
            ResourceHandler.ForErrorMessage("This Feather page is no longer available.", HttpStatusCode.NotFound);
    }
}
