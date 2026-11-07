using System.Windows;
using System.Windows.Input;
using FeatherBrowser.Features.Engine;
using FeatherBrowser.Features.Security;
using FeatherBrowser.Presentation.Tabs;

namespace FeatherBrowser.Presentation.Shell;

public partial class MainWindow
{
    private readonly Dictionary<BrowserTab, List<(PermissionRequest Request, string Origin)>> _permissionQueues = [];
    private readonly Dictionary<string, string> _privatePermissionDecisions = new(StringComparer.Ordinal);
    private (BrowserTab Tab, PermissionRequest Request, string Origin)? _shownPermission;
    private readonly Dictionary<BrowserTab, string> _findQueries = [];


    private static string PermissionDisplayName(string kind) => kind switch
    {
        "Camera" => "Camera",
        "Microphone" => "Microphone",
        "Geolocation" => "Location",
        "Notifications" => "Notifications",
        "ScreenCapture" => "Screen sharing",
        "ClipboardRead" => "Clipboard",
        "MidiSystemExclusiveMessages" => "MIDI devices",
        "MultipleAutomaticDownloads" => "Automatic downloads",
        "WindowManagement" => "Window management",
        "LocalFonts" => "Local fonts",
        "OtherSensors" => "Motion sensors",
        "FileReadWrite" => "Files",
        "StorageAccess" => "Storage",
        "IdleDetection" => "Idle detection",
        "KeyboardLock" => "Keyboard lock",
        "PointerLock" => "Mouse lock",
        "ProtectedMedia" => "Protected content",
        "LocalNetwork" => "Local network devices",
        "ProtocolHandler" => "Link handling",
        "ImmersiveSession" => "VR/AR",
        _ => kind
    };

    private static string PermissionVerb(string kind) => kind switch
    {
        "Camera" => "use your camera",
        "Microphone" => "use your microphone",
        "Geolocation" => "know your location",
        "Notifications" => "show notifications",
        "ScreenCapture" => "share your screen",
        "ClipboardRead" => "see text and images copied to the clipboard",
        "MidiSystemExclusiveMessages" => "control MIDI devices",
        "MultipleAutomaticDownloads" => "download multiple files",
        "WindowManagement" => "manage windows on your displays",
        "LocalFonts" => "use fonts installed on your computer",
        "OtherSensors" => "use motion sensors",
        "FileReadWrite" => "edit files on your computer",
        "StorageAccess" => "use cookies and site data",
        "KeyboardLock" => "capture your keyboard",
        "PointerLock" => "hide and lock your mouse pointer",
        "LocalNetwork" => "connect to devices on your network",
        "ProtocolHandler" => "open a type of link",
        "ImmersiveSession" => "start a VR/AR session",
        _ => $"use {PermissionDisplayName(kind).ToLowerInvariant()}"
    };

    private string? StoredDecision(string origin, string kind)
    {
        string key = SitePermissionPolicy.PermissionKey(origin, kind);
        if (_isPrivateMode)
            return _privatePermissionDecisions.TryGetValue(key, out string? value) ? value : null;
        return _store.Settings.SitePermissions.TryGetValue(key, out string? stored) ? stored : null;
    }

    private void RememberDecision(string origin, IEnumerable<string> kinds, string decision)
    {
        foreach (string kind in kinds)
        {
            string key = SitePermissionPolicy.PermissionKey(origin, kind);
            if (_isPrivateMode)
                _privatePermissionDecisions[key] = decision;
            else
                _store.Settings.SitePermissions[key] = decision;
        }

        if (!_isPrivateMode)
            _store.SaveSettings();
    }

    private void HandlePermissionRequest(BrowserTab tab, PermissionRequest request)
    {
        if (!SitePermissionPolicy.TryNormalizeOrigin(request.Origin, out string origin))
        {
            request.Respond(false);
            return;
        }

        if (request.Kinds.Count == 0)
        {
            request.Respond(false);
            return;
        }

        bool needsPrompt = false;
        foreach (string kind in request.Kinds)
        {
            string? decision = StoredDecision(origin, kind);
            if (string.Equals(decision, "Block", StringComparison.OrdinalIgnoreCase) ||
                (decision is null && kind == "Notifications" && _store.Settings.BlockNotificationPrompts))
            {
                request.Respond(false);
                if (tab == _activeTab)
                    StatusText.Text = $"Blocked {PermissionDisplayName(kind).ToLowerInvariant()} for {SafeHost(origin)}";
                return;
            }

            if (!string.Equals(decision, "Allow", StringComparison.OrdinalIgnoreCase))
                needsPrompt = true;
        }

        if (!needsPrompt)
        {
            request.Respond(true);
            return;
        }

        if (!_permissionQueues.TryGetValue(tab, out var queue))
            _permissionQueues[tab] = queue = [];
        queue.Add((request, origin));

        if (tab == _activeTab)
            RefreshPermissionBar(tab);
        else
            tab.StateIndicator.ToolTip = $"{SafeHost(origin)} is asking for permission";
    }

    private void DismissPermissionPrompt(BrowserTab tab, ulong? promptId)
    {
        if (!_permissionQueues.TryGetValue(tab, out var queue))
            return;

        foreach (var entry in queue.Where(entry => promptId is null || entry.Request.PromptId == promptId).ToList())
        {
            entry.Request.Respond(null);
            queue.Remove(entry);
        }

        if (queue.Count == 0)
            _permissionQueues.Remove(tab);

        if (_shownPermission?.Tab == tab)
            RefreshPermissionBar(_activeTab);
    }

    private void RefreshPermissionBar(BrowserTab? tab)
    {
        _shownPermission = null;

        if (tab is null || !_permissionQueues.TryGetValue(tab, out var queue))
        {
            PermissionBar.Visibility = Visibility.Collapsed;
            return;
        }

        queue.RemoveAll(entry => entry.Request.IsAnswered);
        if (queue.Count == 0)
        {
            _permissionQueues.Remove(tab);
            PermissionBar.Visibility = Visibility.Collapsed;
            return;
        }

        (PermissionRequest request, string origin) = queue[0];
        _shownPermission = (tab, request, origin);

        string verbs = string.Join(" and ", request.Kinds.Select(PermissionVerb));
        PermissionText.Text = $"{SafeHost(origin)} wants to {verbs}";
        PermissionIcon.Text = request.Kinds.FirstOrDefault() switch
        {
            "Camera" => "",
            "Microphone" => "",
            "Geolocation" => "",
            "Notifications" => "",
            "ScreenCapture" => "",
            "ClipboardRead" => "",
            _ => ""
        };
        PermissionDetail.Text = _isPrivateMode
            ? "This choice lasts until you close this private window."
            : "Your choice is remembered for this site. Change it any time from the site menu in the address bar.";
        PermissionBar.Visibility = Visibility.Visible;
    }

    private void AnswerShownPermission(bool? allow)
    {
        if (_shownPermission is not { } shown)
            return;

        if (allow is bool decision)
            RememberDecision(shown.Origin, shown.Request.Kinds, decision ? "Allow" : "Block");

        shown.Request.Respond(allow);
        if (_permissionQueues.TryGetValue(shown.Tab, out var queue))
            queue.RemoveAll(entry => ReferenceEquals(entry.Request, shown.Request));

        StatusText.Text = allow switch
        {
            true => $"Allowed {string.Join(", ", shown.Request.Kinds.Select(PermissionDisplayName)).ToLowerInvariant()} for {SafeHost(shown.Origin)}",
            false => $"Blocked {string.Join(", ", shown.Request.Kinds.Select(PermissionDisplayName)).ToLowerInvariant()} for {SafeHost(shown.Origin)}",
            null => "Permission request dismissed"
        };

        RefreshPermissionBar(_activeTab);
        if (_activeTab is { IsLoaded: true } active)
            active.View.Focus();
    }

    private void PermissionAllowButton_Click(object sender, RoutedEventArgs e) => AnswerShownPermission(true);

    private void PermissionBlockButton_Click(object sender, RoutedEventArgs e) => AnswerShownPermission(false);

    private void PermissionDismissButton_Click(object sender, RoutedEventArgs e) => AnswerShownPermission(null);


    private void OpenFindBar()
    {
        if (_activeTab is not { IsLoaded: true, IsInternalPage: false } tab)
            return;

        FindBar.Visibility = Visibility.Visible;
        string query = _findQueries.TryGetValue(tab, out string? previous) ? previous : FindBox.Text;
        FindBox.Text = query;
        FindBox.Focus();
        Keyboard.Focus(FindBox);
        FindBox.SelectAll();
        if (query.Length > 0)
            tab.View.Find(query);
    }

    private void CloseFindBar()
    {
        if (_activeTab is { IsLoaded: true } tab)
        {
            tab.View.StopFinding(clearSelection: false);
            _findQueries.Remove(tab);
            tab.View.Focus();
        }

        FindBar.Visibility = Visibility.Collapsed;
        FindCount.Text = string.Empty;
    }

    private void RefreshFindBar(BrowserTab tab)
    {
        bool open = _findQueries.ContainsKey(tab) && !tab.IsInternalPage;
        FindBar.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        FindCount.Text = string.Empty;
        if (open)
        {
            FindBox.Text = _findQueries[tab];
            tab.View.Find(FindBox.Text);
        }
    }

    private void FindInPage(bool forward, bool findNext)
    {
        if (_activeTab is not { IsLoaded: true, IsInternalPage: false } tab)
            return;

        if (FindBar.Visibility != Visibility.Visible)
        {
            OpenFindBar();
            return;
        }

        string query = FindBox.Text;
        _findQueries[tab] = query;
        tab.View.Find(query, forward, findNext);
        if (query.Length == 0)
            FindCount.Text = string.Empty;
    }

    private void UpdateFindResult(FindResultArgs result)
    {
        if (FindBar.Visibility != Visibility.Visible || FindBox.Text.Length == 0)
            return;

        FindCount.Text = result.Count == 0 ? "No results" : $"{Math.Max(result.ActiveMatch, 1)} of {result.Count}";
    }

    private void FindBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) =>
        FindInPage(forward: true, findNext: false);

    private void FindBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            FindInPage(forward: !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift), findNext: true);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CloseFindBar();
            e.Handled = true;
        }
    }

    private void FindNextButton_Click(object sender, RoutedEventArgs e) => FindInPage(forward: true, findNext: true);

    private void FindPreviousButton_Click(object sender, RoutedEventArgs e) => FindInPage(forward: false, findNext: true);

    private void FindCloseButton_Click(object sender, RoutedEventArgs e) => CloseFindBar();
}
