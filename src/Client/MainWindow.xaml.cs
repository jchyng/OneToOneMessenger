using Microsoft.UI.Xaml;
using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;

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
        ShowFromTrayCommand = new RelayCommand(ShowFromTray);
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Closing += AppWindow_Closing;

        // Navigate the root frame to the main page on startup.
        RootFrame.Navigate(typeof(MainPage));
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

    private void TrayExit_Click(object sender, RoutedEventArgs e)
    {
        _allowClose = true;
        TrayIcon.Dispose();
        Close();
    }
}
