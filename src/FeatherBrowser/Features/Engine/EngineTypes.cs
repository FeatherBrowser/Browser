using System.Windows.Input;
using CefSharp;

namespace FeatherBrowser.Features.Engine;

internal enum WebResourceKind
{
    Document,
    SubDocument,
    Stylesheet,
    Script,
    Image,
    Font,
    Media,
    XmlHttpRequest,
    Ping,
    Other
}

internal readonly record struct NetworkVerdict(bool IsBlocked, string? StubMimeType = null, ReadOnlyMemory<byte> StubBody = default)
{
    public static readonly NetworkVerdict Allow = new(false);
    public static readonly NetworkVerdict Block = new(true);

    public bool HasStub => IsBlocked && StubMimeType is not null;
}

internal interface INetworkPolicy
{
    bool SendDoNotTrack { get; }

    NetworkVerdict Evaluate(Uri request, string? topLevelAddress, WebResourceKind kind);

    bool ShouldBlock(Uri request, string? topLevelAddress, WebResourceKind kind) =>
        Evaluate(request, topLevelAddress, kind).IsBlocked;

    string? GetScriptletScript(string? frameAddress, string? topLevelAddress);

    string? RewriteNavigation(string address);
}

internal sealed class NavigationCompletedArgs(bool isSuccess, string? errorText, int httpStatusCode) : EventArgs
{
    public bool IsSuccess { get; } = isSuccess;
    public string? ErrorText { get; } = errorText;
    public int HttpStatusCode { get; } = httpStatusCode;
}

internal sealed class NewWindowRequestedArgs(string url, bool inBackground) : EventArgs
{
    public string Url { get; } = url;
    public bool InBackground { get; } = inBackground;
}

internal sealed class WebMessageArgs(string json, string frameUrl, bool isMainFrame) : EventArgs
{
    public string MessageJson { get; } = json;
    public string FrameUrl { get; } = frameUrl;
    public bool IsMainFrame { get; } = isMainFrame;
}

internal sealed class ShortcutArgs(Key key, ModifierKeys modifiers) : EventArgs
{
    public Key Key { get; } = key;
    public ModifierKeys Modifiers { get; } = modifiers;
}

internal sealed class FindResultArgs(int count, int activeMatch, bool final) : EventArgs
{
    public int Count { get; } = count;
    public int ActiveMatch { get; } = activeMatch;
    public bool IsFinal { get; } = final;
}

internal enum DownloadState
{
    InProgress,
    Completed,
    Cancelled,
    Interrupted
}

internal sealed class DownloadEventArgs(int id, string path, string url, DownloadState state, int percent, long receivedBytes, long totalBytes) : EventArgs
{
    public int Id { get; } = id;
    public string FilePath { get; } = path;
    public string Url { get; } = url;
    public DownloadState State { get; } = state;
    public int PercentComplete { get; } = percent;
    public long ReceivedBytes { get; } = receivedBytes;
    public long TotalBytes { get; } = totalBytes;
}

internal sealed class PermissionRequest
{
    private readonly Action<bool?> _respond;
    private int _answered;

    internal PermissionRequest(ulong? promptId, string origin, IReadOnlyList<string> kinds, Action<bool?> respond)
    {
        PromptId = promptId;
        Origin = origin;
        Kinds = kinds;
        _respond = respond;
    }

    public ulong? PromptId { get; }
    public string Origin { get; }
    public IReadOnlyList<string> Kinds { get; }
    public bool IsAnswered => Volatile.Read(ref _answered) != 0;

    public void Respond(bool? allow)
    {
        if (Interlocked.Exchange(ref _answered, 1) == 0)
            _respond(allow);
    }

    internal static IReadOnlyList<string> KindsFrom(PermissionRequestType types)
    {
        var kinds = new List<string>();
        void Add(PermissionRequestType flag, string name)
        {
            if (types.HasFlag(flag) && !kinds.Contains(name))
                kinds.Add(name);
        }

        Add(PermissionRequestType.CameraStream, "Camera");
        Add(PermissionRequestType.CameraPanTiltZoom, "Camera");
        Add(PermissionRequestType.MicStream, "Microphone");
        Add(PermissionRequestType.Geolocation, "Geolocation");
        Add(PermissionRequestType.Notifications, "Notifications");
        Add(PermissionRequestType.Clipboard, "ClipboardRead");
        Add(PermissionRequestType.MidiSysex, "MidiSystemExclusiveMessages");
        Add(PermissionRequestType.MultipleDownloads, "MultipleAutomaticDownloads");
        Add(PermissionRequestType.WindowManagement, "WindowManagement");
        Add(PermissionRequestType.LocalFonts, "LocalFonts");
        Add(PermissionRequestType.Sensors, "OtherSensors");
        Add(PermissionRequestType.FileSystemAccess, "FileReadWrite");
        Add(PermissionRequestType.StorageAccess, "StorageAccess");
        Add(PermissionRequestType.TopLevelStorageAccess, "StorageAccess");
        Add(PermissionRequestType.IdleDetection, "IdleDetection");
        Add(PermissionRequestType.KeyboardLock, "KeyboardLock");
        Add(PermissionRequestType.PointerLock, "PointerLock");
        Add(PermissionRequestType.ProtectedMediaIdentifier, "ProtectedMedia");
        Add(PermissionRequestType.LocalNetworkAccess, "LocalNetwork");
        Add(PermissionRequestType.LocalNetwork, "LocalNetwork");
        Add(PermissionRequestType.LoopbackNetwork, "LocalNetwork");
        Add(PermissionRequestType.RegisterProtocolHandler, "ProtocolHandler");
        Add(PermissionRequestType.ArSession, "ImmersiveSession");
        Add(PermissionRequestType.VrSession, "ImmersiveSession");
        Add(PermissionRequestType.HandTracking, "ImmersiveSession");
        Add(PermissionRequestType.CapturedSurfaceControl, "ScreenCapture");
        Add(PermissionRequestType.IdentityProvider, "IdentityProvider");
        Add(PermissionRequestType.WebAppInstallation, "AppInstallation");
        Add(PermissionRequestType.DiskQuota, "StorageAccess");

        if (kinds.Count == 0)
            kinds.Add(types.ToString());

        return kinds;
    }

    internal static IReadOnlyList<string> KindsFrom(MediaAccessPermissionType types)
    {
        var kinds = new List<string>();
        if (types.HasFlag(MediaAccessPermissionType.VideoCapture))
            kinds.Add("Camera");
        if (types.HasFlag(MediaAccessPermissionType.AudioCapture))
            kinds.Add("Microphone");
        if (types.HasFlag(MediaAccessPermissionType.DesktopVideoCapture) ||
            types.HasFlag(MediaAccessPermissionType.DesktopAudioCapture))
            kinds.Add("ScreenCapture");
        return kinds;
    }
}

internal sealed record LoginSubmission(string Origin, string Username, string Password)
{
    public override string ToString() => $"LoginSubmission {{ Origin = {Origin}, Username = {Username} }}";
}

internal static class PageScripts
{
    public static readonly string AudioMarker = "__feather_audio_" + Guid.NewGuid().ToString("N") + ":";
    public static readonly string NavigationMarker = "__feather_nav_" + Guid.NewGuid().ToString("N") + ":";
    public static readonly string EditMarker = "__feather_edit_" + Guid.NewGuid().ToString("N") + ":";
    public static readonly string PipMarker = "__feather_pip_" + Guid.NewGuid().ToString("N") + ":";
    public static readonly string LoginMarker = "__feather_login_" + Guid.NewGuid().ToString("N") + ":";
    public static readonly string AudioMonitor = """
        (() => {
          if (window.__featherAudioMonitor) return;
          Object.defineProperty(window, "__featherAudioMonitor", { value: true });
          const marker = "__MARKER__";
          const media = [];
          const contexts = [];
          let last = null;
          let timer = 0;
          const track = (element) => {
            if (element && !media.some((ref) => ref.deref() === element)) media.push(new WeakRef(element));
          };
          const audible = () => {
            for (const element of document.querySelectorAll("video,audio")) track(element);
            for (let i = media.length - 1; i >= 0; i--) {
              const element = media[i].deref();
              if (!element || !element.isConnected) { media.splice(i, 1); continue; }
              if (!element.paused && !element.ended && !element.muted && element.volume > 0) return true;
            }
            for (let i = contexts.length - 1; i >= 0; i--) {
              const context = contexts[i].deref();
              if (!context || context.state === "closed") { contexts.splice(i, 1); continue; }
              if (context.state === "running") return true;
            }
            return false;
          };
          const report = () => {
            timer = 0;
            const playing = audible();
            if (playing !== last) {
              last = playing;
              console.debug(marker + (playing ? "1" : "0"));
            }
          };
          const schedule = () => { if (!timer) timer = setTimeout(report, 250); };
          for (const type of ["play", "playing", "pause", "ended", "volumechange", "emptied"]) {
            document.addEventListener(type, (event) => { track(event.target); schedule(); }, true);
          }
          const play = HTMLMediaElement.prototype.play;
          HTMLMediaElement.prototype.play = function () { track(this); schedule(); return play.apply(this, arguments); };
          for (const name of ["AudioContext", "webkitAudioContext"]) {
            const Original = window[name];
            if (typeof Original !== "function") continue;
            window[name] = new Proxy(Original, {
              construct(target, args, newTarget) {
                const context = Reflect.construct(target, args, newTarget);
                contexts.push(new WeakRef(context));
                context.addEventListener("statechange", schedule);
                return context;
              }
            });
          }
          setInterval(() => { if (last) schedule(); }, 5000);

          const editMarker = "__EDIT__";
          const skipped = ["button", "submit", "reset", "checkbox", "radio", "range", "color", "file", "image", "hidden", "search", "password"];
          const edits = [];
          let edited = false;
          let editTimer = 0;
          const isEditable = (element) => !!element && (element.isContentEditable ||
            element.tagName === "TEXTAREA" ||
            (element.tagName === "INPUT" && !skipped.includes((element.type || "text").toLowerCase())));
          const changed = (element) => element.isContentEditable
            ? (element.textContent || "").trim().length > 0
            : (element.value || "") !== (element.defaultValue || "");
          const checkEdits = () => {
            editTimer = 0;
            for (let i = edits.length - 1; i >= 0; i--) {
              const element = edits[i].deref();
              if (!element || !element.isConnected || !changed(element)) edits.splice(i, 1);
            }
            const now = edits.length > 0;
            if (now !== edited) {
              edited = now;
              console.debug(editMarker + (now ? "1" : "0"));
            }
            if (now) editTimer = setTimeout(checkEdits, 3000);
          };
          document.addEventListener("input", (event) => {
            if (!event.isTrusted) return;
            const element = event.composedPath()[0];
            if (!isEditable(element)) return;
            if (!edits.some((ref) => ref.deref() === element)) edits.push(new WeakRef(element));
            clearTimeout(editTimer);
            editTimer = setTimeout(checkEdits, 400);
          }, true);
          document.addEventListener("submit", () => { edits.length = 0; clearTimeout(editTimer); checkEdits(); }, true);

          const pipMarker = "__PIP__";
          document.addEventListener("enterpictureinpicture", () => console.debug(pipMarker + "1"), true);
          document.addEventListener("leavepictureinpicture", () => console.debug(pipMarker + "0"), true);
          if (window === window.top) {
            window.addEventListener("mouseup", (event) => {
              if (event.button !== 3 && event.button !== 4) return;
              event.preventDefault();
              console.debug("__NAV__" + (event.button === 3 ? "back" : "forward"));
            }, true);
          }
        })();
        """;

    public static string AudioMonitorScript => AudioMonitor
        .Replace("__MARKER__", AudioMarker, StringComparison.Ordinal)
        .Replace("__NAV__", NavigationMarker, StringComparison.Ordinal)
        .Replace("__EDIT__", EditMarker, StringComparison.Ordinal)
        .Replace("__PIP__", PipMarker, StringComparison.Ordinal);
    public static readonly string LoginCapture = """
        (() => {
          if (window.__featherLoginCapture) return;
          Object.defineProperty(window, "__featherLoginCapture", { value: true });
          const marker = "__LOGIN__";
          let lastSent = "";
          const shown = (el) => !!el && el.getClientRects().length > 0;
          const capture = (root) => {
            const scope = root && root.querySelectorAll ? root : document;
            const passwords = [...scope.querySelectorAll('input[type="password"]')].filter((p) => p.value && shown(p));
            if (!passwords.length) return;
            const pass = passwords[passwords.length - 1];
            const fields = [...scope.querySelectorAll("input")];
            let user = "";
            for (let i = fields.indexOf(passwords[0]) - 1; i >= 0; i--) {
              const type = (fields[i].type || "text").toLowerCase();
              if ((type === "text" || type === "email" || type === "tel") && fields[i].value && shown(fields[i])) {
                user = fields[i].value.trim();
                break;
              }
            }
            if (!user) {
              const named = scope.querySelector('input[autocomplete="username"],input[autocomplete="email"]');
              if (named && named.value) user = named.value.trim();
            }
            const payload = JSON.stringify({ o: location.origin, u: user, p: pass.value });
            if (payload === lastSent) return;
            lastSent = payload;
            console.debug(marker + payload);
          };
          document.addEventListener("submit", (event) => capture(event.target), true);
          document.addEventListener("click", (event) => {
            const button = event.target && event.target.closest && event.target.closest('button,input[type="submit"]');
            if (button) capture(button.form || button.closest("form") || document);
          }, true);
          document.addEventListener("keydown", (event) => {
            if (event.key === "Enter" && event.target && event.target.type === "password")
              capture(event.target.form || document);
          }, true);
        })();
        """;

    public static string LoginCaptureScript => LoginCapture.Replace("__LOGIN__", LoginMarker, StringComparison.Ordinal);

    public const string ExitFullscreen =
        "if (document.fullscreenElement) { document.exitFullscreen().catch(() => {}); }";
}
