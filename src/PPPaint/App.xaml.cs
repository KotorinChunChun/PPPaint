using PPPaint.Core;
using Microsoft.UI.Xaml;

namespace PPPaint;

public partial class App : Application
{
    private MainWindow? _window;
    private Mutex? _instanceMutex;
    private ManagementEndpoint? _management;
    internal static LaunchOptions Options { get; private set; } = null!;

    public App()
    {
        InitializeDiagnostics();
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try { Options = LaunchOptions.Parse(Environment.GetCommandLineArgs(), Environment.GetEnvironmentVariable("PPPAINT_DATA_DIRECTORY")); }
        catch (ArgumentException) { Environment.ExitCode = 2; Exit(); return; }
        try
        {
            _instanceMutex = new Mutex(true, ManagementEndpoint.MutexName(Options), out var first);
            if (!first)
            {
                Environment.ExitCode = await ManagementEndpoint.ForwardAsync(Options) ? 0 : 3;
                _instanceMutex.Dispose(); _instanceMutex = null;
                Exit(); return;
            }
            if (!Options.IsolatedSettings) SettingsMigration.TryCopyLegacy(Options.SettingsDirectory);
            var mainWindow = new MainWindow();
            _window = mainWindow;
            mainWindow.Closed += (_, _) =>
            {
                _management?.Dispose();
                _instanceMutex?.ReleaseMutex(); _instanceMutex?.Dispose(); _instanceMutex = null;
                _window = null;
                Exit();
            };
            _window.Activate();
            _management = new ManagementEndpoint(mainWindow, Options);
            var startupPath = Options.FilePath;
            if (startupPath is not null)
            {
                await mainWindow.ManagementOpenAsync(startupPath);
            }
        }
        catch (Exception exception)
        {
            WriteStartupFailure(exception);
            _management?.Dispose();
            _instanceMutex?.Dispose(); _instanceMutex = null;
            Environment.ExitCode = 1;
            Exit();
        }
    }
}
