using PPPaint.Core;
using Microsoft.UI.Xaml;

namespace PPPaint;

public partial class App : Application
{
    private EditorWindow? _window;
    private readonly List<EditorWindow> _windows = [];
    internal static App Instance => (App)Current;
    internal IReadOnlyList<EditorWindow> Windows => _windows;
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
            var mainWindow = CreateWindow();
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

    internal EditorWindow CreateWindow(ScreenshotFrame? frame = null)
    {
        var window = new EditorWindow(frame);
        _windows.Add(window);
        window.Closed += (_, _) =>
        {
            _windows.Remove(window);
            if (ReferenceEquals(_window, window))
            {
                _management?.Dispose(); _management = null; _window = null;
                if (_windows.Count > 0) { _window = _windows[0]; _management = new ManagementEndpoint(_window, Options); }
            }
            if (_windows.Count == 0)
            {
                _management?.Dispose();
                _instanceMutex?.ReleaseMutex(); _instanceMutex?.Dispose(); _instanceMutex = null;
                Exit();
            }
        };
        window.Activate();
        if (_window is null) { _window = window; _management = new ManagementEndpoint(window, Options); }
        return window;
    }
}
