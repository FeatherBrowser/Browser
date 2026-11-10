using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using CefSharp;
using System.Windows.Threading;
using CefSharp.Handler;

namespace FeatherBrowser.Features.Engine;

internal sealed partial class WebPage
{
    private const CefMenuCommand OpenLinkInNewTab = CefMenuCommand.UserFirst + 1;
    private const CefMenuCommand CopyLinkAddress = CefMenuCommand.UserFirst + 2;
    private const CefMenuCommand SearchSelection = CefMenuCommand.UserFirst + 3;
    private const CefMenuCommand OpenImageInNewTab = CefMenuCommand.UserFirst + 4;
    private const CefMenuCommand InspectElement = CefMenuCommand.UserFirst + 5;
    private const CefMenuCommand CopyImageAddress = CefMenuCommand.UserFirst + 6;

    internal static WebResourceKind MapResourceType(ResourceType type) => type switch
    {
        ResourceType.MainFrame or ResourceType.NavigationPreLoadMainFrame => WebResourceKind.Document,
        ResourceType.SubFrame or ResourceType.NavigationPreLoadSubFrame => WebResourceKind.SubDocument,
        ResourceType.Stylesheet => WebResourceKind.Stylesheet,
        ResourceType.Script or ResourceType.Worker or ResourceType.SharedWorker or ResourceType.ServiceWorker => WebResourceKind.Script,
        ResourceType.Image or ResourceType.Favicon => WebResourceKind.Image,
        ResourceType.FontResource => WebResourceKind.Font,
        ResourceType.Media => WebResourceKind.Media,
        ResourceType.Xhr => WebResourceKind.XmlHttpRequest,
        ResourceType.Ping or ResourceType.CspReport => WebResourceKind.Ping,
        _ => WebResourceKind.Other
    };

    private static bool IsWebScheme(Uri uri) =>
        uri.Scheme is "http" or "https" or "ws" or "wss";

    private sealed class RequestHandlerImpl(WebPage page) : RequestHandler
    {
        protected override IResourceRequestHandler GetResourceRequestHandler(
            IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request,
            bool isNavigation, bool isDownload, string requestInitiator, ref bool disableDefaultHandling) =>
            page._resourceHandler;

        protected override bool OnBeforeBrowse(
            IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request, bool userGesture, bool isRedirect)
        {
            if (browser.IsPopup || frame is null || !frame.IsMain)
                return false;

            string url = request.Url ?? string.Empty;

            if (InternalPages.TryParsePageUrl(url, out string kind, out string nonce))
            {
                if (!string.Equals(nonce, page._internalNonce, StringComparison.Ordinal))
                {
                    TransitionType transition = request.TransitionType;
                    bool history = transition.HasFlag(TransitionType.ForwardBack) ||
                                   (transition & TransitionType.SourceMask) == TransitionType.Reload;
                    if (history)
                        page.Post(() => page.InternalPageRequested?.Invoke(page, kind));
                    return true;
                }
            }
            else if (!InternalPages.IsInternalUrl(url) &&
                     string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase) &&
                     page._options.Network.RewriteNavigation(url) is { } rewritten &&
                     !string.Equals(rewritten, url, StringComparison.Ordinal))
            {
                page.Post(() => page.Browser.LoadUrl(rewritten));
                return true;
            }

            page.Post(() => page.NavigationStarting?.Invoke(page, url));
            return false;
        }

        protected override bool OnOpenUrlFromTab(
            IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, string targetUrl,
            WindowOpenDisposition targetDisposition, bool userGesture)
        {
            if (targetDisposition is not (WindowOpenDisposition.NewBackgroundTab or WindowOpenDisposition.NewForegroundTab or WindowOpenDisposition.NewWindow))
                return false;

            bool background = targetDisposition == WindowOpenDisposition.NewBackgroundTab;
            page.Post(() => page.NewWindowRequested?.Invoke(page, new NewWindowRequestedArgs(targetUrl, background)));
            return true;
        }

        protected override void OnRenderProcessTerminated(
            IWebBrowser chromiumWebBrowser, IBrowser browser, CefTerminationStatus status, int errorCode, string errorMessage)
        {
            if (browser.IsPopup)
                return;

            string reason = string.IsNullOrWhiteSpace(errorMessage) ? status.ToString() : $"{status}: {errorMessage}";
            page.Post(() => page.ProcessFailed?.Invoke(page, reason));
        }
    }

    private sealed class ResourceRequestHandlerImpl(WebPage page) : ResourceRequestHandler
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<ulong, NetworkVerdict> _stubs = new();

        protected override IResourceHandler? GetResourceHandler(
            IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request)
        {
            if (!_stubs.TryRemove(request.Identifier, out NetworkVerdict verdict))
                return null;

            var handler = new ResourceHandler(
                verdict.StubMimeType!,
                new MemoryStream(verdict.StubBody.ToArray(), writable: false),
                autoDisposeStream: true)
            {
                StatusCode = 200,
                StatusText = "OK"
            };

            string origin = request.GetHeaderByName("Origin");
            if (string.IsNullOrEmpty(origin))
            {
                handler.Headers.Add("Access-Control-Allow-Origin", "*");
            }
            else
            {
                handler.Headers.Add("Access-Control-Allow-Origin", origin);
                handler.Headers.Add("Access-Control-Allow-Credentials", "true");
                handler.Headers.Add("Vary", "Origin");
            }

            handler.Headers.Add("Cache-Control", "no-store");
            return handler;
        }

        protected override void OnResourceLoadComplete(
            IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request, IResponse response,
            UrlRequestStatus status, long receivedContentLength) =>
            _stubs.TryRemove(request.Identifier, out _);

        protected override CefReturnValue OnBeforeResourceLoad(
            IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request, IRequestCallback callback)
        {
            string url = request.Url;
            if (InternalPages.IsInternalUrl(url))
                return CefReturnValue.Continue;

            INetworkPolicy policy = page._options.Network;
            if (policy.SendDoNotTrack)
                request.SetHeaderByName("DNT", "1", overwrite: true);

            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || !IsWebScheme(uri))
                return CefReturnValue.Continue;

            WebResourceKind kind = MapResourceType(request.ResourceType);
            string? topLevel = kind == WebResourceKind.Document ? url : page._source;

            NetworkVerdict verdict = policy.Evaluate(uri, topLevel, kind);
            if (!verdict.IsBlocked)
            {
                _stubs.TryRemove(request.Identifier, out _);
                return CefReturnValue.Continue;
            }

            page.OnRequestBlocked();

            if (verdict.HasStub)
            {
                _stubs[request.Identifier] = verdict;
                return CefReturnValue.Continue;
            }

            return CefReturnValue.Cancel;
        }

        protected override bool OnProtocolExecution(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request)
        {
            string url = request.Url;
            page.Post(() => page.ExternalProtocolRequested?.Invoke(page, url));
            return false;
        }
    }

    private sealed class LifeSpanHandlerImpl(WebPage page) : LifeSpanHandler
    {
        protected override bool OnBeforePopup(
            IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, string targetUrl, string targetFrameName,
            WindowOpenDisposition targetDisposition, bool userGesture, IPopupFeatures popupFeatures, IWindowInfo windowInfo,
            IBrowserSettings browserSettings, ref bool noJavascriptAccess, out IWebBrowser? newBrowser)
        {
            newBrowser = null;

            if (targetDisposition == WindowOpenDisposition.NewPopup && userGesture)
                return false;

            if (!userGesture)
                return true;

            bool background = targetDisposition == WindowOpenDisposition.NewBackgroundTab;
            page.Post(() => page.NewWindowRequested?.Invoke(page, new NewWindowRequestedArgs(targetUrl, background)));
            return true;
        }

        protected override void OnAfterCreated(IWebBrowser chromiumWebBrowser, IBrowser browser)
        {
            if (!browser.IsPopup)
                page.OnBrowserCreated(browser);
        }

        protected override bool DoClose(IWebBrowser chromiumWebBrowser, IBrowser browser)
        {
            if (browser.IsPopup || page._disposed)
                return false;

            page.Post(() => page.CloseRequested?.Invoke(page, EventArgs.Empty));
            return true;
        }
    }

    private sealed class DisplayHandlerImpl(WebPage page) : DisplayHandler
    {
        protected override void OnAddressChanged(IWebBrowser chromiumWebBrowser, AddressChangedEventArgs args)
        {
            if (args.Browser.IsPopup)
                return;

            page._source = args.Address ?? string.Empty;
            string address = page._source;
            Volatile.Write(ref page._unsupportedMediaReported, 0);
            page.Post(() => page.SourceChanged?.Invoke(page, address));
        }

        protected override void OnTitleChanged(IWebBrowser chromiumWebBrowser, TitleChangedEventArgs args)
        {
            if (args.Browser.IsPopup)
                return;

            page._title = args.Title ?? string.Empty;
            string title = page._title;
            page.Post(() => page.TitleChanged?.Invoke(page, title));
        }

        protected override void OnFaviconUrlChange(IWebBrowser chromiumWebBrowser, IBrowser browser, IList<string> urls)
        {
            if (browser.IsPopup)
                return;

            string[] copy = urls?.ToArray() ?? [];
            page.Post(() => page.FaviconUrlsChanged?.Invoke(page, copy));
        }

        protected override void OnFullscreenModeChange(IWebBrowser chromiumWebBrowser, IBrowser browser, bool fullscreen)
        {
            if (!browser.IsPopup)
                page.Post(() => page.FullscreenChanged?.Invoke(page, fullscreen));
        }

        protected override void OnLoadingProgressChange(IWebBrowser chromiumWebBrowser, IBrowser browser, double progress)
        {
            if (!browser.IsPopup)
                page.Post(() => page.LoadingProgressChanged?.Invoke(page, progress));
        }

        protected override void OnStatusMessage(IWebBrowser chromiumWebBrowser, StatusMessageEventArgs statusMessageArgs)
        {
            if (statusMessageArgs.Browser.IsPopup)
                return;

            string text = statusMessageArgs.Value ?? string.Empty;
            page.Post(() => page.StatusMessageChanged?.Invoke(page, text));
        }

        protected override bool OnConsoleMessage(IWebBrowser chromiumWebBrowser, ConsoleMessageEventArgs consoleMessageArgs)
        {
            string message = consoleMessageArgs.Message ?? string.Empty;
            if (message.StartsWith(PageScripts.NavigationMarker, StringComparison.Ordinal))
            {
                if (consoleMessageArgs.Browser.IsPopup)
                    return true;

                string direction = message[PageScripts.NavigationMarker.Length..];
                page.Post(() =>
                {
                    if (direction == "back")
                        page.GoBack();
                    else if (direction == "forward")
                        page.GoForward();
                });
                return true;
            }

            if (message.StartsWith(PageScripts.LoginMarker, StringComparison.Ordinal))
            {
                if (!consoleMessageArgs.Browser.IsPopup)
                    page.OnLoginSubmitted(message[PageScripts.LoginMarker.Length..]);
                return true;
            }

            if (message.StartsWith(PageScripts.MediaMarker, StringComparison.Ordinal))
            {
                if (!consoleMessageArgs.Browser.IsPopup)
                    page.OnUnsupportedMedia();
                return true;
            }

            if (message.StartsWith(PageScripts.PipMarker, StringComparison.Ordinal))
            {
                string pip = message[PageScripts.PipMarker.Length..];
                int at = pip.LastIndexOf(':');
                if (at > 0 && at == pip.Length - 2)
                {
                    if (pip[^1] == '1')
                        page._pipFrames[pip[..at]] = 0;
                    else
                        page._pipFrames.TryRemove(pip[..at], out _);
                }
                return true;
            }

            if (message.StartsWith(PageScripts.EditMarker, StringComparison.Ordinal))
            {
                string edit = message[PageScripts.EditMarker.Length..];
                int split = edit.LastIndexOf(':');
                if (split > 0 && split == edit.Length - 2)
                {
                    if (edit[^1] == '1')
                        page._editedFrames[edit[..split]] = 0;
                    else
                        page._editedFrames.TryRemove(edit[..split], out _);
                }
                return true;
            }

            if (!message.StartsWith(PageScripts.AudioMarker, StringComparison.Ordinal))
                return false;

            string payload = message[PageScripts.AudioMarker.Length..];
            int separator = payload.LastIndexOf(':');
            if (separator > 0 && separator == payload.Length - 2)
            {
                page._audibleFrames[payload[..separator]] = payload[^1] == '1';
                page.UpdateAudibleState();
            }

            return true;
        }
    }

    private sealed class KeyboardHandlerImpl(WebPage page) : KeyboardHandler
    {
        protected override bool OnPreKeyEvent(
            IWebBrowser chromiumWebBrowser, IBrowser browser, KeyType type, int windowsKeyCode, int nativeKeyCode,
            CefEventFlags modifiers, bool isSystemKey, ref bool isKeyboardShortcut)
        {
            if (type != KeyType.RawKeyDown || browser.IsPopup)
                return false;

            Key key = KeyInterop.KeyFromVirtualKey(windowsKeyCode);
            ModifierKeys mods = ToModifierKeys(modifiers);

            if (!page._options.IsShortcut(key, mods))
                return false;

            if (!page._options.IsReservedShortcut(key, mods))
            {
                isKeyboardShortcut = true;
                return false;
            }

            page.Post(() => page.ShortcutPressed?.Invoke(page, new ShortcutArgs(key, mods)));
            return true;
        }

        protected override bool OnKeyEvent(
            IWebBrowser chromiumWebBrowser, IBrowser browser, KeyType type, int windowsKeyCode, int nativeKeyCode,
            CefEventFlags modifiers, bool isSystemKey)
        {
            if (type != KeyType.RawKeyDown || browser.IsPopup)
                return false;

            Key key = KeyInterop.KeyFromVirtualKey(windowsKeyCode);
            ModifierKeys mods = ToModifierKeys(modifiers);
            if (!page._options.IsShortcut(key, mods))
                return false;

            page.Post(() => page.ShortcutPressed?.Invoke(page, new ShortcutArgs(key, mods)));
            return true;
        }

        private static ModifierKeys ToModifierKeys(CefEventFlags modifiers)
        {
            ModifierKeys mods = ModifierKeys.None;
            if (modifiers.HasFlag(CefEventFlags.ControlDown)) mods |= ModifierKeys.Control;
            if (modifiers.HasFlag(CefEventFlags.ShiftDown)) mods |= ModifierKeys.Shift;
            if (modifiers.HasFlag(CefEventFlags.AltDown)) mods |= ModifierKeys.Alt;
            return mods;
        }
    }

    private sealed class ContextMenuHandlerImpl(WebPage page) : ContextMenuHandler
    {
        protected override void OnBeforeContextMenu(
            IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IContextMenuParams parameters, IMenuModel model)
        {
            if (browser.IsPopup)
                return;

            int index = 0;
            if (!string.IsNullOrEmpty(parameters.LinkUrl))
            {
                model.InsertItemAt(index++, OpenLinkInNewTab, "Open link in new tab");
                model.InsertItemAt(index++, CopyLinkAddress, "Copy link address");
                model.InsertSeparatorAt(index++);
            }

            if (parameters.MediaType == ContextMenuMediaType.Image && !string.IsNullOrEmpty(parameters.SourceUrl))
            {
                model.InsertItemAt(index++, OpenImageInNewTab, "Open image in new tab");
                model.InsertItemAt(index++, CopyImageAddress, "Copy image address");
                model.InsertSeparatorAt(index++);
            }

            string selection = parameters.SelectionText?.Trim() ?? string.Empty;
            if (selection.Length > 0)
            {
                string label = selection.Length > 32 ? selection[..31] + "…" : selection;
                model.InsertItemAt(index++, SearchSelection, $"Search the web for “{label.Replace("&", "&&")}”");
                model.InsertSeparatorAt(index);
            }

            model.Remove(CefMenuCommand.ViewSource);
            if (model.Count > 0)
                model.AddSeparator();
            model.AddItem(InspectElement, "Inspect");
        }

        protected override bool OnContextMenuCommand(
            IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IContextMenuParams parameters,
            CefMenuCommand commandId, CefEventFlags eventFlags)
        {
            string link = parameters.LinkUrl ?? string.Empty;
            string source = parameters.SourceUrl ?? string.Empty;
            string selection = parameters.SelectionText?.Trim() ?? string.Empty;
            int x = parameters.XCoord;
            int y = parameters.YCoord;

            switch (commandId)
            {
                case OpenLinkInNewTab:
                    page.Post(() => page.NewWindowRequested?.Invoke(page, new NewWindowRequestedArgs(link, inBackground: true)));
                    return true;
                case OpenImageInNewTab:
                    page.Post(() => page.NewWindowRequested?.Invoke(page, new NewWindowRequestedArgs(source, inBackground: true)));
                    return true;
                case CopyLinkAddress:
                    page.Post(() => CopyText(link));
                    return true;
                case CopyImageAddress:
                    page.Post(() => CopyText(source));
                    return true;
                case SearchSelection:
                    page.Post(() => page.SearchRequested?.Invoke(page, selection));
                    return true;
                case InspectElement:
                    browser.GetHost().ShowDevTools(windowInfo: null, inspectElementAtX: x, inspectElementAtY: y);
                    return true;
                default:
                    return false;
            }
        }

        protected override bool RunContextMenu(
            IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IContextMenuParams parameters,
            IMenuModel model, IRunContextMenuCallback callback)
        {
            if (browser.IsPopup)
                return false;

            List<MenuEntry> entries = ReadMenu(model);
            page._dispatcher.BeginInvoke(DispatcherPriority.Normal, () => page.ShowContextMenu(entries, callback));
            return true;
        }

        private static void CopyText(string text)
        {
            try
            {
                if (!string.IsNullOrEmpty(text))
                    Clipboard.SetText(text);
            }
            catch (System.Runtime.InteropServices.ExternalException ex)
            {
                Trace.TraceWarning($"Clipboard is busy: {ex.Message}");
            }
        }
    }

    private sealed class DownloadHandlerImpl(WebPage page) : DownloadHandler
    {
        protected override bool CanDownload(IWebBrowser chromiumWebBrowser, IBrowser browser, string url, string requestMethod) => true;

        protected override bool OnBeforeDownload(
            IWebBrowser chromiumWebBrowser, IBrowser browser, DownloadItem downloadItem, IBeforeDownloadCallback callback)
        {
            if (callback.IsDisposed)
                return false;

            string folder = page._options.DownloadFolder();
            string path;
            bool showDialog = false;
            try
            {
                Directory.CreateDirectory(folder);
                path = DownloadPaths.Unique(folder, downloadItem.SuggestedFileName);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Trace.TraceWarning($"Download folder unavailable: {ex.Message}");
                path = string.Empty;
                showDialog = true;
            }

            using (callback)
                callback.Continue(path, showDialog);

            int id = downloadItem.Id;
            string url = downloadItem.Url ?? string.Empty;
            long total = downloadItem.TotalBytes;
            page._downloads[id] = new DownloadProgress(-1, 0);
            page.Post(() => page.DownloadStarting?.Invoke(page, new DownloadEventArgs(id, path, url, DownloadState.InProgress, 0, 0, total)));
            return true;
        }

        protected override void OnDownloadUpdated(
            IWebBrowser chromiumWebBrowser, IBrowser browser, DownloadItem downloadItem, IDownloadItemCallback callback)
        {
            int id = downloadItem.Id;
            if (!page._downloads.TryGetValue(id, out DownloadProgress previous))
                return;

            DownloadState state =
                downloadItem.IsComplete ? DownloadState.Completed :
                downloadItem.IsCancelled ? DownloadState.Cancelled :
                downloadItem.IsInterrupted ? DownloadState.Interrupted :
                DownloadState.InProgress;

            if (state == DownloadState.InProgress)
            {
                long now = Environment.TickCount64;
                int bucket = downloadItem.PercentComplete >= 0 ? downloadItem.PercentComplete / 5 : -1;
                bool due = bucket >= 0 ? bucket != previous.Bucket : now - previous.ReportedAt >= 500;
                if (!due)
                    return;

                page._downloads[id] = new DownloadProgress(bucket, now);
            }
            else
            {
                page._downloads.TryRemove(id, out _);
            }

            var args = new DownloadEventArgs(
                id,
                downloadItem.FullPath ?? string.Empty,
                downloadItem.Url ?? string.Empty,
                state,
                Math.Max(0, downloadItem.PercentComplete),
                downloadItem.ReceivedBytes,
                downloadItem.TotalBytes);

            page.Post(() => page.DownloadUpdated?.Invoke(page, args));
        }
    }

    private sealed class PermissionHandlerImpl(WebPage page) : PermissionHandler
    {
        protected override bool OnShowPermissionPrompt(
            IWebBrowser chromiumWebBrowser, IBrowser browser, ulong promptId, string requestingOrigin,
            PermissionRequestType requestedPermissions, IPermissionPromptCallback callback)
        {
            PermissionRequest? request = null;
            request = new PermissionRequest(promptId, requestingOrigin ?? string.Empty, PermissionRequest.KindsFrom(requestedPermissions), allow =>
            {
                page._pendingPermissions.TryRemove(request!, out _);
                using (callback)
                {
                    callback.Continue(allow is not bool allowed
                        ? PermissionRequestResult.Dismiss
                        : allowed ? PermissionRequestResult.Accept : PermissionRequestResult.Deny);
                }
            });

            page._pendingPermissions[request] = 0;
            page.Post(() => page.PermissionRequested?.Invoke(page, request));
            return true;
        }

        protected override bool OnRequestMediaAccessPermission(
            IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, string requestingOrigin,
            MediaAccessPermissionType requestedPermissions, IMediaAccessCallback callback)
        {
            PermissionRequest? request = null;
            request = new PermissionRequest(null, requestingOrigin ?? string.Empty, PermissionRequest.KindsFrom(requestedPermissions), allow =>
            {
                page._pendingPermissions.TryRemove(request!, out _);
                using (callback)
                {
                    if (allow == true)
                        callback.Continue(requestedPermissions);
                    else
                        callback.Cancel();
                }
            });

            page._pendingPermissions[request] = 0;
            page.Post(() => page.PermissionRequested?.Invoke(page, request));
            return true;
        }

        protected override void OnDismissPermissionPrompt(
            IWebBrowser chromiumWebBrowser, IBrowser browser, ulong promptId, PermissionRequestResult result)
        {
            page.Post(() => page.PermissionPromptDismissed?.Invoke(page, promptId));
        }
    }

    private sealed class FindHandlerImpl(WebPage page) : FindHandler
    {
        protected override void OnFindResult(
            IWebBrowser chromiumWebBrowser, IBrowser browser, int identifier, int count, CefSharp.Structs.Rect selectionRect,
            int activeMatchOrdinal, bool finalUpdate)
        {
            page.Post(() => page.FindResultChanged?.Invoke(page, new FindResultArgs(count, activeMatchOrdinal, finalUpdate)));
        }
    }

    private sealed class RenderProcessHandlerImpl(WebPage page) : IRenderProcessMessageHandler
    {
        public void OnContextCreated(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame)
        {
            if (browser.IsPopup || frame is null)
                return;

            string frameUrl = frame.Url ?? string.Empty;
            if (!InternalPages.IsInternalUrl(frameUrl) &&
                page._options.Network.GetScriptletScript(frameUrl, frame.IsMain ? frameUrl : page._source) is { } scriptlets)
            {
                frame.ExecuteJavaScriptAsync(scriptlets, "feather://shield-scriptlets", 1);
            }

            string frameKey = frame.Identifier.ToString();
            string script = PageScripts.AudioMonitorScript
                .Replace(PageScripts.AudioMarker, PageScripts.AudioMarker + frameKey + ":", StringComparison.Ordinal)
                .Replace(PageScripts.EditMarker, PageScripts.EditMarker + frameKey + ":", StringComparison.Ordinal)
                .Replace(PageScripts.PipMarker, PageScripts.PipMarker + frameKey + ":", StringComparison.Ordinal);
            frame.ExecuteJavaScriptAsync(script, "feather://audio-monitor", 1);

            if (frame.IsMain && !InternalPages.IsInternalUrl(frameUrl) &&
                frameUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                frame.ExecuteJavaScriptAsync(PageScripts.LoginCaptureScript, "feather://login-capture", 1);
            }

            if (frame.IsMain)
            {
                string url = frame.Url ?? string.Empty;
                page.Post(() => page.DocumentCreated?.Invoke(page, url));
            }
        }

        public void OnContextReleased(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame)
        {
            if (frame is not null)
            {
                page._editedFrames.TryRemove(frame.Identifier.ToString(), out _);
                page._pipFrames.TryRemove(frame.Identifier.ToString(), out _);
            }

            if (frame is not null && page._audibleFrames.TryRemove(frame.Identifier.ToString(), out _))
                page.UpdateAudibleState();
        }

        public void OnFocusedNodeChanged(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IDomNode node)
        {
        }

        public void OnUncaughtException(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, JavascriptException exception)
        {
        }
    }
}
