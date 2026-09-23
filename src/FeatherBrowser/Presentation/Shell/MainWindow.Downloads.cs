using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Controls.Primitives;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;

namespace FeatherBrowser.Presentation.Shell;

public partial class MainWindow : Window
{
    private void OpenDownloadsFolder()
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private void OpenDownloadedFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                return;
            }

            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
            else
                StatusText.Text = "Downloaded file is no longer available";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Could not open downloaded file";
            MessageBox.Show($"Feather could not open this download.\n\n{ex.Message}", "Downloads", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ClearDownloadList()
    {
        if (_store.Downloads.Count == 0)
            return;
        if (MessageBox.Show("Clear Feather's download history?\n\nDownloaded files will not be deleted.", "Clear downloads", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        _store.ClearDownloads();
        StatusText.Text = "Download history cleared";
    }
}
