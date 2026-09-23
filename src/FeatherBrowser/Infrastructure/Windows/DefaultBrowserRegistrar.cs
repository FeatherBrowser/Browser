using System.Diagnostics;
using Microsoft.Win32;

namespace FeatherBrowser.Infrastructure.Windows;

internal static class DefaultBrowserRegistrar
{
    private const string AppName = "Feather Browser";
    private const string UrlProgId = "FeatherBrowserURL";
    private const string HtmlProgId = "FeatherBrowserHTML";

    public static void RegisterAndOpenSettings()
    {
        string exe =
            Environment.ProcessPath ??
            Process.GetCurrentProcess().MainModule?.FileName ??
            throw new InvalidOperationException(
                "Could not determine Feather Browser executable path.");

        string command = $"\"{exe}\" \"%1\"";

        CreateProgId(
            UrlProgId,
            "Feather Browser URL",
            exe,
            command,
            isUrl: true);

        CreateProgId(
            HtmlProgId,
            "Feather Browser HTML Document",
            exe,
            command,
            isUrl: false);

        using (var capabilities =
               Registry.CurrentUser.CreateSubKey(
                   @"Software\FeatherBrowser\Capabilities"))
        {
            capabilities.SetValue(
                "ApplicationName",
                AppName);

            capabilities.SetValue(
                "ApplicationDescription",
                "A lightweight Chromium-compatible browser focused on low background resource use.");

            using var url =
                capabilities.CreateSubKey("URLAssociations");

            url.SetValue("http", UrlProgId);
            url.SetValue("https", UrlProgId);

            using var files =
                capabilities.CreateSubKey("FileAssociations");

            files.SetValue(".htm", HtmlProgId);
            files.SetValue(".html", HtmlProgId);
        }

        using (var registered =
               Registry.CurrentUser.CreateSubKey(
                   @"Software\RegisteredApplications"))
        {
            registered.SetValue(
                AppName,
                @"Software\FeatherBrowser\Capabilities");
        }

        using (var client =
               Registry.CurrentUser.CreateSubKey(
                   @"Software\Clients\StartMenuInternet\FeatherBrowser"))
        {
            client.SetValue("", AppName);

            using var icon =
                client.CreateSubKey("DefaultIcon");

            icon.SetValue("", $"\"{exe}\",0");

            using var open =
                client.CreateSubKey(@"shell\open\command");

            open.SetValue("", $"\"{exe}\"");
        }

        OpenDefaultAppsSettings();
    }

    private static void CreateProgId(
        string progId,
        string description,
        string exe,
        string command,
        bool isUrl)
    {
        using var key =
            Registry.CurrentUser.CreateSubKey(
                $@"Software\Classes\{progId}");

        key.SetValue("", description);

        if (isUrl)
        {
            key.SetValue("URL Protocol", "");
        }

        using var icon =
            key.CreateSubKey("DefaultIcon");

        icon.SetValue("", $"\"{exe}\",0");

        using var open =
            key.CreateSubKey(@"shell\open\command");

        open.SetValue("", command);
    }

    private static void OpenDefaultAppsSettings()
    {
        string target =
            "ms-settings:defaultapps?registeredAppUser=" +
            Uri.EscapeDataString(AppName);

        try
        {
            Process.Start(
                new ProcessStartInfo(target)
                {
                    UseShellExecute = true
                });
        }
        catch (InvalidOperationException)
        {
            OpenGeneralDefaultAppsSettings();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            OpenGeneralDefaultAppsSettings();
        }
    }

    private static void OpenGeneralDefaultAppsSettings()
    {
        Process.Start(
            new ProcessStartInfo("ms-settings:defaultapps")
            {
                UseShellExecute = true
            });
    }
}