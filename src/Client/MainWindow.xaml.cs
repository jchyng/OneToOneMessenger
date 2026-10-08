using Microsoft.UI.Xaml;
using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using Windows.Graphics;
using System.Diagnostics;
using System.Threading;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace OneToOneMessenger_Client;

/// <summary>
/// The application window. This hosts a Frame that displays pages. Add your
/// UI and logic to MainPage.xaml / MainPage.xaml.cs instead of here so you
/// can use Page features such as navigation events and the Loaded lifecycle.
/// </summary>
public sealed partial class MainWindow : Window
{
    private bool _allowClose;
    private int _exitRequested;

    public RelayCommand ShowFromTrayCommand { get; }
    public AsyncRelayCommand ExitFromTrayCommand { get; }

    public MainWindow()
    {
        try
        {
            Debug.WriteLine("[MainWindow] Constructor started");
            ShowFromTrayCommand = new RelayCommand(ShowFromTray);
            ExitFromTrayCommand = new AsyncRelayCommand(RequestExitAsync);
            InitializeComponent();
            Debug.WriteLine("[MainWindow] InitializeComponent done");

            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
            Debug.WriteLine("[MainWindow] TitleBar set");

            AppWindow.SetIcon("Assets/AppIcon.ico");
            AppWindow.Closing += AppWindow_Closing;
            Debug.WriteLine("[MainWindow] Icon set, Closing event hooked");

            // Navigate the root frame to the main page on startup.
            RootFrame.Navigate(typeof(MainPage));
            Debug.WriteLine("[MainWindow] Navigated to MainPage");

            // Ensure window is visible and activated
            this.Activate();
            Debug.WriteLine("[MainWindow] Window activated");

            AppWindow.MoveAndResize(new RectInt32(100, 100, 1280, 800));
            Debug.WriteLine("[MainWindow] MoveAndResize done");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MainWindow] EXCEPTION: {ex}");
            throw;
        }
    }

    private void AppWindow_Closing(
        Microsoft.UI.Windowing.AppWindow sender,
        Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
    {
        if (_allowClose)
        {
            return;
        }

        args.Cancel = true;
        this.Hide();
        TrayIcon.ForceCreate();
    }

    private void ShowFromTray()
    {
        this.Show();
        Activate();
    }

    /// <summary>
    /// Requests a full application exit.  This is intentionally separate from the
    /// normal window-close path, which only hides the window in the notification area.
    /// </summary>
    public async Task RequestExitAsync()
    {
        if (Interlocked.Exchange(ref _exitRequested, 1) != 0)
        {
            Debug.WriteLine("[MainWindow] RequestExitAsync: already requested, returning");
            return;
        }

        Debug.WriteLine("[MainWindow] RequestExitAsync: starting exit sequence");
        _allowClose = true;
        try
        {
            await ExitAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MainWindow] RequestExitAsync error: {ex}");
        }
    }

    private async Task ExitAsync()
    {
        try
        {
            // A tray-icon failure must never prevent the application from exiting.
            // Dispose on background thread to avoid blocking UI thread.
            _ = Task.Run(() =>
            {
                try
                {
                    TrayIcon.Dispose();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MainWindow] Could not dispose tray icon: {ex}");
                }
            });

            if (RootFrame.Content is MainPage page)
            {
                // SignalR cleanup can wait on an unavailable server. Do not leave the
                // application resident forever when the user explicitly chose Exit.
                var shutdownTask = page.ShutdownAsync();
                var completedTask = await Task.WhenAny(shutdownTask, Task.Delay(TimeSpan.FromSeconds(3)));
                
                if (completedTask == shutdownTask)
                {
                    // Shutdown completed normally, propagate any exception
                    await shutdownTask;
                }
                else
                {
                    Debug.WriteLine("[MainWindow] Shutdown timed out after 3 seconds, forcing exit");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MainWindow] Shutdown failed: {ex}");
        }
        finally
        {
            // In WinUI 3 / Windows App SDK, the application does NOT automatically exit
            // when the last window closes. We must explicitly call Exit().
            try
            {
                Debug.WriteLine("[MainWindow] Calling Application.Current.Exit()");
                Application.Current.Exit();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainWindow] Application.Exit() failed: {ex}");
            }

            // Close the window - this will trigger AppWindow_Closing with _allowClose=true
            // which allows the window to actually close.
            Close();

            // WinUI 3의 Application.Exit()이 신뢰성 있게 작동하지 않을 수 있으므로
            // 강제 종료를 최후 수단으로 사용합니다.
            // Application.Exit()이 정상 작동하면 여기까지 오지 않습니다.
            Debug.WriteLine("[MainWindow] Forcing process exit via Environment.Exit(0)");
            Environment.Exit(0);
        }
    }
}
