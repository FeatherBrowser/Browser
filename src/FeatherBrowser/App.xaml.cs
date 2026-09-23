using System.IO.Pipes;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Windows;
using FeatherBrowser.Presentation.Shell;

namespace FeatherBrowser;

public partial class App : Application
{
    private const string InstanceMutexName = "Local\\FeatherBrowser.SingleInstance.1E3F66AB";
    private const string InstancePipeName = "FeatherBrowser.SingleInstance.1E3F66AB";
    private Mutex? _instanceMutex;
    private CancellationTokenSource? _ipcCancellation;

    protected override void OnStartup(StartupEventArgs e)
    {
        bool createdNew;
        _instanceMutex = new Mutex(true, InstanceMutexName, out createdNew);
        if (!createdNew)
        {
            SendToPrimary(e.Args);
            Shutdown(0);
            return;
        }

        base.OnStartup(e);
        _ipcCancellation = new CancellationTokenSource();
        _ = ListenForSecondaryInstancesAsync(_ipcCancellation.Token);

        InstanceRequest request = ParseRequest(e.Args);
        var window = new MainWindow(request.Address, request.PrivateMode);
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _ipcCancellation?.Cancel(); } catch { }
        _ipcCancellation?.Dispose();
        try { _instanceMutex?.ReleaseMutex(); } catch { }
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }

    private static InstanceRequest ParseRequest(string[] args)
    {
        bool privateMode = args.Any(x => string.Equals(x, "--private", StringComparison.OrdinalIgnoreCase));
        string? address = args.FirstOrDefault(x =>
            x.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            x.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            x.StartsWith("file://", StringComparison.OrdinalIgnoreCase));
        return new InstanceRequest { PrivateMode = privateMode, Address = address };
    }

    private static void SendToPrimary(string[] args)
    {
        string payload = JsonSerializer.Serialize(ParseRequest(args));
        for (int attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", InstancePipeName, PipeDirection.Out);
                client.Connect(350);
                using var writer = new StreamWriter(client) { AutoFlush = true };
                writer.WriteLine(payload);
                return;
            }
            catch
            {
                Thread.Sleep(100);
            }
        }
    }

    private async Task ListenForSecondaryInstancesAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    InstancePipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(token);
                using var reader = new StreamReader(server);
                string? json = await reader.ReadLineAsync(token);
                if (string.IsNullOrWhiteSpace(json))
                    continue;

                InstanceRequest? request = JsonSerializer.Deserialize<InstanceRequest>(json);
                if (request is null)
                    continue;

                Task dispatched = await Dispatcher.InvokeAsync(() => HandleSecondaryRequestAsync(request));
                await dispatched;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                if (!token.IsCancellationRequested)
                    await Task.Delay(150, token);
            }
        }
    }

    private async Task HandleSecondaryRequestAsync(InstanceRequest request)
    {
        if (request.PrivateMode)
        {
            var privateWindow = new MainWindow(request.Address, privateMode: true);
            privateWindow.Show();
            ActivateWindow(privateWindow);
            return;
        }

        MainWindow? target = Windows.OfType<MainWindow>().FirstOrDefault(x => !x.IsPrivateMode);
        if (target is null)
        {
            target = new MainWindow(request.Address, privateMode: false);
            MainWindow = target;
            target.Show();
        }
        else
        {
            await target.OpenExternalAddressAsync(request.Address);
        }

        ActivateWindow(target);
    }

    private static void ActivateWindow(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        if (!window.IsVisible)
            window.Show();
        window.Activate();
        window.Topmost = true;
        window.Topmost = false;
        window.Focus();
    }

    private sealed class InstanceRequest
    {
        public bool PrivateMode { get; set; }
        public string? Address { get; set; }
    }
}
