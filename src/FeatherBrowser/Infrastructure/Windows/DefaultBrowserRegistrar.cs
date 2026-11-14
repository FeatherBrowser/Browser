
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace FeatherBrowser.Infrastructure.Windows;

[SupportedOSPlatform("windows")]
internal static class DefaultBrowserRegistrar
{
    private const string AppName = "Feather Browser";
    private const string AppId = "FeatherBrowser";

    private const string UrlProgId = "FeatherBrowserURL";
    private const string HtmlProgId = "FeatherBrowserHTML";

    private const string Description =
        "A lightweight Chromium-compatible browser focused " +
        "on low background resource use.";

    private const string CapabilitiesPath =
        @"Software\FeatherBrowser\Capabilities";

    private const string RegisteredAppsPath =
        @"Software\RegisteredApplications";

    private const string StartMenuPath =
        @"Software\Clients\StartMenuInternet\FeatherBrowser";

    private static readonly Guid AssociationRegistrationClsid =
        new("591209C7-767B-42B2-9FBA-44EE4615F2C7");

    private const uint ShcneAssocChanged = 0x08000000;
    private const uint ShcnfIdList = 0x0000;

    public static bool IsFeatherDefaultForWeb()
    {
        object? instance = null;

        try
        {
            Type? type = Type.GetTypeFromCLSID(
                AssociationRegistrationClsid);

            if (type is null)
                return false;

            instance = Activator.CreateInstance(type);

            if (instance is not IApplicationAssociationRegistration association)
                return false;

            return IsDefaultProtocol(association, "http") &&
                   IsDefaultProtocol(association, "https");
        }
        catch (Exception ex) when (
            ex is COMException or
                  InvalidCastException or
                  UnauthorizedAccessException or
                  TypeLoadException)
        {
            return false;
        }
        finally
        {
            if (instance is not null && Marshal.IsComObject(instance))
            {
                Marshal.ReleaseComObject(instance);
            }
        }
    }

    public static void RegisterAndOpenSettings()
    {
        Register();
        OpenDefaultAppsSettings();
    }

    public static void Register()
    {
        string exe = GetExecutablePath();

        string executable = Quote(exe);
        string openCommand = $"{executable} \"%1\"";
        string icon = $"{executable},0";

        RegisterProgId(
            UrlProgId,
            "Feather Browser URL",
            icon,
            openCommand,
            isUrl: true);

        RegisterProgId(
            HtmlProgId,
            "Feather Browser HTML Document",
            icon,
            openCommand,
            isUrl: false);

        RegisterCapabilities();
        RegisterStartMenuClient(executable, icon);

        SetRegistryString(
            RegisteredAppsPath,
            AppName,
            CapabilitiesPath);

        NotifyAssociationsChanged();
    }

    private static bool IsDefaultProtocol(
        IApplicationAssociationRegistration association,
        string protocol)
    {
        IntPtr result = IntPtr.Zero;

        try
        {
            int hr = association.QueryCurrentDefault(
                protocol,
                AssociationType.UrlProtocol,
                AssociationLevel.Effective,
                out result);

            if (hr < 0 || result == IntPtr.Zero)
                return false;

            string? progId = Marshal.PtrToStringUni(result);

            return string.Equals(
                progId,
                UrlProgId,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (result != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(result);
            }
        }
    }

    private static void RegisterProgId(
        string progId,
        string description,
        string icon,
        string command,
        bool isUrl)
    {
        string root = $@"Software\Classes\{progId}";

        SetRegistryString(root, "", description);

        if (isUrl)
        {
            SetRegistryString(root, "URL Protocol", "");
        }

        SetRegistryString(
            $@"{root}\DefaultIcon",
            "",
            icon);

        SetRegistryString(
            $@"{root}\shell\open\command",
            "",
            command);
    }

    private static void RegisterCapabilities()
    {
        SetRegistryString(
            CapabilitiesPath,
            "ApplicationName",
            AppName);

        SetRegistryString(
            CapabilitiesPath,
            "ApplicationDescription",
            Description);

        string urls = $@"{CapabilitiesPath}\URLAssociations";

        SetRegistryString(urls, "http", UrlProgId);
        SetRegistryString(urls, "https", UrlProgId);

        string files = $@"{CapabilitiesPath}\FileAssociations";

        SetRegistryString(files, ".htm", HtmlProgId);
        SetRegistryString(files, ".html", HtmlProgId);

        string startMenu = $@"{CapabilitiesPath}\Startmenu";

        SetRegistryString(
            startMenu,
            "StartMenuInternet",
            AppId);
    }

    private static void RegisterStartMenuClient(
        string executable,
        string icon)
    {
        SetRegistryString(
            StartMenuPath,
            "",
            AppName);

        SetRegistryString(
            $@"{StartMenuPath}\DefaultIcon",
            "",
            icon);

        SetRegistryString(
            $@"{StartMenuPath}\shell\open\command",
            "",
            executable);
    }

    private static void SetRegistryString(
        string path,
        string name,
        string value)
    {
        using RegistryKey key =
            Registry.CurrentUser.CreateSubKey(path)
            ?? throw new IOException(
                $"Could not create registry key: {path}");

        key.SetValue(
            name,
            value,
            RegistryValueKind.String);
    }

    private static string GetExecutablePath()
    {
        string path = Environment.ProcessPath
            ?? throw new InvalidOperationException(
                "Could not determine Feather Browser executable path.");

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "Feather Browser executable could not be found.",
                path);
        }

        return path;
    }

    private static string Quote(string value)
    {
        return $"\"{value}\"";
    }

    private static void NotifyAssociationsChanged()
    {
        SHChangeNotify(
            ShcneAssocChanged,
            ShcnfIdList,
            IntPtr.Zero,
            IntPtr.Zero);
    }

    private static void OpenDefaultAppsSettings()
    {
        string appSettings =
            "ms-settings:defaultapps?registeredAppUser=" +
            Uri.EscapeDataString(AppName);

        if (TryOpenSettings(appSettings))
            return;

        if (TryOpenSettings("ms-settings:defaultapps"))
            return;

        throw new InvalidOperationException(
            "Could not open Windows Default Apps settings.");
    }

    private static bool TryOpenSettings(string uri)
    {
        try
        {
            using Process? process = Process.Start(
                new ProcessStartInfo
                {
                    FileName = uri,
                    UseShellExecute = true
                });

            return process is not null;
        }
        catch (Exception ex) when (
            ex is Win32Exception or
                  InvalidOperationException or
                  IOException)
        {
            return false;
        }
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(
        uint eventId,
        uint flags,
        IntPtr item1,
        IntPtr item2);

    private enum AssociationType
    {
        FileExtension = 0,
        UrlProtocol = 1
    }

    private enum AssociationLevel
    {
        Machine = 0,
        Effective = 1,
        User = 2
    }

    [ComImport]
    [Guid("4E530B0A-E611-4C77-A3AC-9031D022281B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationAssociationRegistration
    {
        [PreserveSig]
        int QueryCurrentDefault(
            [MarshalAs(UnmanagedType.LPWStr)] string query,
            AssociationType associationType,
            AssociationLevel associationLevel,
            out IntPtr progId);
    }
}
