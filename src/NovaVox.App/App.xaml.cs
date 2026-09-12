using System.Windows;
using NovaVox.App.Tray;

namespace NovaVox.App;

public partial class App : Application
{
    private TrayIconService? _tray;

    public MainWindow? MainAppWindow { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        MainAppWindow = new MainWindow();
        MainWindow = MainAppWindow;

        _tray = new TrayIconService(onShow: ShowMainWindow, onQuit: QuitApplication);

        MainAppWindow.Show();
    }

    /// <summary>Ramène la fenêtre principale au premier plan (menu tray "Afficher NOVAVOX", double-clic).</summary>
    public void ShowMainWindow()
    {
        if (MainAppWindow is null) return;
        MainAppWindow.Show();
        if (MainAppWindow.WindowState == WindowState.Minimized)
            MainAppWindow.WindowState = WindowState.Normal;
        MainAppWindow.Activate();
    }

    /// <summary>Fermeture réelle de l'application (menu tray "Quitter"), contrairement au bouton X de la fenêtre qui masque seulement.</summary>
    public void QuitApplication()
    {
        if (MainAppWindow is not null) MainAppWindow.IsQuitting = true;
        _tray?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        base.OnExit(e);
    }
}
