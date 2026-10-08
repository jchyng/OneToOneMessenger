using Microsoft.UI.Xaml;
using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using Windows.Graphics;
using System.Diagnostics;

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

    public RelayCommand ShowFromTrayCommand { get; }

    public MainWindow()
    {
        try
        {
            Debug.WriteLine("[MainWindow] Constructor started");
            ShowFromTrayCommand = new RelayCommand(ShowFromTray);
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

    private void TrayOpen_Click(object sender, RoutedEventArgs e)
    {
        ShowFromTray();
    }

    private async void TrayExit_Click(object sender, RoutedEventArgs e)
    {
        _allowClose = true;
        TrayIcon.Dispose();
        try
        {
            if (RootFrame.Content is MainPage page)
            {
                await page.ShutdownAsync();
            }
        }
        finally
        {
            Close();
            Application.Current.Exit();
        }
    }
}
