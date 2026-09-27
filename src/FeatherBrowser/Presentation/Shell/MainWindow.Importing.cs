using System.Diagnostics;
using System.IO;
using System.Windows;
using FeatherBrowser.Domain.Models;
using FeatherBrowser.Infrastructure.Windows;
using FeatherBrowser.Presentation.Tabs;
using FeatherImport;
using FeatherImport.Edge;
using Microsoft.Win32;

namespace FeatherBrowser.Presentation.Shell;

public partial class MainWindow : Window
{
    private async Task ImportFromEdgeAsync()
    {
        try
        {
            StatusText.Text = "Importing from Microsoft Edge…";

            BrowserImportResult result = await Task.Run(
                static () => new EdgeImporter().Import());

            int bookmarksImported = _store.MergeBookmarks(
                result.Bookmarks.Select(bookmark => new BrowserBookmark
                {
                    Title = bookmark.Title,
                    Url = bookmark.Url,
                    Folder = bookmark.Folder,
                    CreatedAt = bookmark.CreatedAt
                }));

            int historyImported = _store.MergeHistory(
                result.History.Select(entry => new HistoryEntry
                {
                    Title = entry.Title,
                    Url = entry.Url,
                    LastVisited = entry.LastVisited,
                    VisitCount = entry.VisitCount
                }));

            StatusText.Text =
                $"Imported {bookmarksImported} favorites, " +
                $"{historyImported} history entries and found " +
                $"{result.SessionUrls.Count} Edge tabs";

            string warning = result.Warnings.Count == 0
                ? string.Empty
                : "\n\nSome profile data could not be read. " +
                  "Close Edge completely and try again if you want to retry:\n• " +
                  string.Join("\n• ", result.Warnings.Take(4));

            string tabText = result.SessionUrls.Count == 0
                ? "No recoverable Edge session tabs were found."
                : $"Found {result.SessionUrls.Count} URLs in Edge's recent session files.";

            MessageBoxResult choice = MessageBox.Show(
                $"Edge import finished.\n\n" +
                $"Profiles scanned: {result.ProfilesScanned}\n" +
                $"New favorites: {bookmarksImported}\n" +
                $"New history entries: {historyImported}\n" +
                $"{tabText}\n\n" +
                "Website login cookies cannot be copied directly because current Edge " +
                "protects local browser data with application-bound encryption. " +
                "Feather keeps its own cookies and login sessions persistently once you sign in here.\n\n" +
                "Passwords can be moved using the Passwords > Import passwords from Edge CSV option." +
                warning +
                (result.SessionUrls.Count > 0
                    ? "\n\nOpen the recovered Edge tabs in Feather now?"
                    : string.Empty),
                "Import from Edge",
                result.SessionUrls.Count > 0
                    ? MessageBoxButton.YesNo
                    : MessageBoxButton.OK,
                MessageBoxImage.Information);

            if (result.SessionUrls.Count > 0 &&
                choice == MessageBoxResult.Yes)
            {
                foreach (string url in result.SessionUrls.Take(20))
                    await AddTabAsync(url, select: false);

                BrowserTab? newest = _tabs
                    .LastOrDefault(tab => !tab.IsClosed);

                if (newest is not null)
                    await SelectTabAsync(newest);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "Edge import failed";

            MessageBox.Show(
                $"Feather could not import Edge data.\n\n" +
                $"{ex.Message}\n\n" +
                "If Edge is open, close it completely and try again.",
                "Import from Edge",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ImportPasswordsFromEdgeCsv()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select the password CSV exported from Microsoft Edge",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            PasswordImportResult result =
                _passwordVault.ImportEdgeCsv(dialog.FileName);

            StatusText.Text =
                $"Password migration: {result.Imported} imported, " +
                $"{result.Updated} updated";

            MessageBoxResult delete = MessageBox.Show(
                $"Password import finished.\n\n" +
                $"New passwords: {result.Imported}\n" +
                $"Updated passwords: {result.Updated}\n" +
                $"Skipped rows: {result.Skipped}\n\n" +
                "Feather stored the passwords encrypted for your current Windows account. " +
                "The Edge CSV is still a plaintext file. Delete that CSV now?",
                "Password migration",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (delete != MessageBoxResult.Yes)
                return;

            try
            {
                File.Delete(dialog.FileName);

                StatusText.Text =
                    "Passwords imported · plaintext Edge CSV deleted";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Passwords were imported, but Feather could not delete the CSV.\n\n" +
                    $"{ex.Message}\n\n" +
                    "Delete the CSV manually when you are finished with it.",
                    "Password migration",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Feather could not import that password CSV.\n\n{ex.Message}",
                "Password migration",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OpenEdgePasswordExport()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "msedge.exe",
                Arguments = "edge://wallet/passwords",
                UseShellExecute = true
            });

            StatusText.Text =
                "In Edge, use Passwords > Export passwords, " +
                "then import the CSV into Feather";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not open Microsoft Edge's password manager.\n\n" +
                $"{ex.Message}\n\n" +
                "Open Edge manually, go to Passwords, and choose Export passwords.",
                "Password migration",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private void RegisterAsDefaultBrowser()
    {
        try
        {
            DefaultBrowserRegistrar.RegisterAndOpenSettings();

            StatusText.Text =
                "Feather registered · choose Feather Browser in Windows Default Apps";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not register Feather with Windows Default Apps.\n\n{ex.Message}",
                "Feather Browser",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}