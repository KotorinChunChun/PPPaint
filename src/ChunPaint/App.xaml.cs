using ChunPaint.Core;
using Microsoft.UI.Xaml;

namespace ChunPaint;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var mainWindow = new MainWindow();
        _window = mainWindow;
        _window.Activate();

        var startupPath = StartupFileRequest.FindSupportedPath(Environment.GetCommandLineArgs());
        if (startupPath is not null)
        {
            _ = mainWindow.OpenStartupFileAsync(startupPath);
        }
    }
}
