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
using FeatherBrowser.Presentation.Pages;
using FeatherBrowser.Presentation.Tabs;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;

namespace FeatherBrowser.Presentation.Shell;

public partial class MainWindow : Window
{
    private void ShowStartPage(BrowserTab tab)
    {
        tab.IsStartPage = true;
        tab.IsSettingsPage = false;
        tab.IsLibraryPage = false;
        tab.IsWelcomePage = false;
        tab.LastAddress = string.Empty;
        tab.Title.Text = "New Tab";
        tab.NeedsContentRestore = true;

        if (tab.IsLoaded)
        {
            (string name, string prefix) = SearchEngineInfo();
            tab.View.NavigateToString(StartPage.Html(name, prefix, _store.Settings));
            tab.NeedsContentRestore = false;
        }

        if (_activeTab == tab)
        {
            AddressBox.Clear();
            Title = "New Tab — Feather";
            TitleText.Text = "New Tab";
            UpdateBookmarkButton();
        }
    }

    private void ShowWelcomePage(BrowserTab tab)
    {
        tab.IsStartPage = false;
        tab.IsSettingsPage = false;
        tab.IsLibraryPage = false;
        tab.IsWelcomePage = true;
        tab.LastAddress = string.Empty;
        tab.Title.Text = "Welcome";
        tab.NeedsContentRestore = true;

        if (tab.IsLoaded)
        {
            tab.View.NavigateToString(WelcomePage.Html());
            tab.NeedsContentRestore = false;
        }

        if (_activeTab == tab)
        {
            AddressBox.Text = "feather://welcome";
            Title = "Welcome — Feather";
            TitleText.Text = "Welcome";
            UpdateBookmarkButton();
        }
    }

    private void ShowSettingsPage(BrowserTab tab)
    {
        tab.IsStartPage = false;
        tab.IsSettingsPage = true;
        tab.IsLibraryPage = false;
        tab.IsWelcomePage = false;
        tab.LastAddress = string.Empty;
        tab.Title.Text = "Settings";
        tab.NeedsContentRestore = true;

        if (tab.IsLoaded)
        {
            tab.View.NavigateToString(SettingsPage.Html(_store.Settings, _store.Bookmarks.Count, _store.History.Count, _passwordVault.Count));
            tab.NeedsContentRestore = false;
        }

        if (_activeTab == tab)
        {
            AddressBox.Text = "feather://settings";
            Title = "Settings — Feather";
            TitleText.Text = "Settings";
            UpdateBookmarkButton();
        }
    }

    private async Task OpenSettingsAsync()
    {
        if (_activeTab is null)
        {
            BrowserTab? tab = await AddTabAsync(select: true);
            if (tab is not null)
                ShowSettingsPage(tab);
            return;
        }

        ShowSettingsPage(_activeTab);
    }

    private void ShowLibraryPage(BrowserTab tab, string section)
    {
        section = section is "downloads" or "favorites" ? section : "history";
        tab.IsStartPage = false;
        tab.IsSettingsPage = false;
        tab.IsLibraryPage = true;
        tab.IsWelcomePage = false;
        tab.LibrarySection = section;
        tab.LastAddress = string.Empty;
        tab.Title.Text = LibraryTitle(section);
        tab.NeedsContentRestore = true;

        if (tab.IsLoaded)
        {
            tab.View.NavigateToString(LibraryPage.Html(_store.Bookmarks, _store.History, _store.Downloads, section));
            tab.NeedsContentRestore = false;
        }

        if (_activeTab == tab)
        {
            AddressBox.Text = $"feather://{section}";
            Title = $"{LibraryTitle(section)} — Feather";
            TitleText.Text = LibraryTitle(section);
            UpdateBookmarkButton();
        }
    }

    private async Task OpenLibraryAsync(string section)
    {
        if (_activeTab is null)
        {
            BrowserTab? tab = await AddTabAsync($"feather://{section}", select: true);
            if (tab is not null)
                ShowLibraryPage(tab, section);
            return;
        }

        ShowLibraryPage(_activeTab, section);
    }
}
