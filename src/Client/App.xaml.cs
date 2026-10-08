using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace OneToOneMessenger_Client;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private Window? _window;
    public static Window? CurrentWindow { get; private set; }
    
    // P/Invoke to attach to parent console (for Ctrl+C support when launched from terminal)
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint dwProcessId);
    
    private const uint ATTACH_PARENT_PROCESS = 0xFFFFFFFF;

    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        InitializeComponent();

        // Global exception handlers
        this.UnhandledException += App_UnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        
        // Try to attach to parent console for Ctrl+C support when launched from terminal
        TryAttachConsole();
        Console.CancelKeyPress += Console_CancelKeyPress;
    }

    private void TryAttachConsole()
    {
        try
        {
            // Attach to parent process console if launched from command prompt/terminal
            // This enables Ctrl+C handling for debugging scenarios
            AttachConsole(ATTACH_PARENT_PROCESS);
        }
        catch
        {
            // Ignore failures - not critical if no console is available
        }
    }

    private void Console_CancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        // A WinUI application's UI objects must only be touched on its UI thread.
        // If the dispatcher is unavailable, leave Cancel false so Windows performs
        // its normal Ctrl+C termination instead of leaving a startup process behind.
        if (_window is not MainWindow window)
        {
            return;
        }

        e.Cancel = window.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.High, async () =>
        {
            await window.RequestExitAsync();
        });
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        LogException(e.Exception);
        e.Handled = true;
    }

    private void CurrentDomain_UnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            LogException(ex);
        }
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogException(e.Exception);
        e.SetObserved();
    }

    private void LogException(Exception ex)
    {
        try
        {
            var logPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OneToOneMessenger", "crash.log");
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(logPath)!);
            File.AppendAllText(logPath, $"[{DateTime.Now}] {ex}\n\n");
            Debug.WriteLine($"[CRASH] {ex}");
        }
        catch { }
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        try
        {
            Debug.WriteLine("[App] OnLaunched started");
            _window = new MainWindow();
            CurrentWindow = _window;
            Debug.WriteLine("[App] MainWindow created");
            _window.Activate();
            Debug.WriteLine("[App] Window activated");
        }
        catch (Exception ex)
        {
            LogException(ex);
            throw;
        }
    }
}
