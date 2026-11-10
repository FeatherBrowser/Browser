using System.Windows.Controls;
using FeatherBrowser.Presentation.Tabs;

namespace FeatherBrowser.Presentation.Shell;

public partial class MainWindow
{
    private bool _samplingTabMemory;

    private void AttachTabMemoryTooltip(BrowserTab tab)
    {
        tab.Header.ToolTip = tab.Title.Text;
        tab.Header.ToolTipOpening += (_, _) => tab.Header.ToolTip = TabTooltip(tab);
        ToolTipService.SetInitialShowDelay(tab.Header, 600);
    }

    private string TabTooltip(BrowserTab tab)
    {
        string host = tab.IsInternalPage ? "Feather" : SafeHost(tab.LastAddress);
        string state =
            !tab.IsLoaded ? "Unloaded · uses no memory" :
            tab.View.IsSuspended ? "Sleeping" :
            tab == _activeTab ? "Active" : "Loaded";
        string memory = tab.IsLoaded && tab.PageMemoryBytes is long bytes ? $" · {FormatBytes(bytes)} page memory" : "";
        return $"{tab.Title.Text}\n{host} · {state}{memory}";
    }

    private async Task SampleTabMemoryAsync()
    {
        if (_samplingTabMemory || _isClosing)
            return;

        _samplingTabMemory = true;
        try
        {
            foreach (BrowserTab tab in _tabs.Where(t => !t.IsClosed).ToList())
            {
                if (!tab.IsLoaded)
                {
                    tab.PageMemoryBytes = null;
                    continue;
                }

                if (tab.View.IsSuspended)
                    continue;

                tab.PageMemoryBytes = await tab.View.GetPageMemoryAsync();
                if (_isClosing)
                    return;
            }
        }
        finally
        {
            _samplingTabMemory = false;
        }
    }
}
