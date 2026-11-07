using FeatherBrowser.Features.Engine;

namespace FeatherBrowser;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        int subprocessExitCode = FeatherEngine.RunSubprocessIfRequested(args);
        if (subprocessExitCode >= 0)
            return subprocessExitCode;

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
