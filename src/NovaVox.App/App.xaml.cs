using System.Windows;
using NovaVox.App.Autolaunch;
using NovaVox.App.Tray;

namespace NovaVox.App;

public partial class App : Application
{
    private TrayIconService? _tray;

    public MainWindow? MainAppWindow { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Lancée avec --wait-for-sc (raccourci de veille, voir
        // StarCitizenAutolaunch) : reste en veille silencieuse, sans
        // aucune fenêtre, jusqu'à détecter Star Citizen — bloquant par
        // conception, comme _wait_for_star_citizen_if_requested (app.py).
        StarCitizenAutolaunch.WaitForStarCitizenIfRequested(e.Args);

        MainAppWindow = new MainWindow();
        MainWindow = MainAppWindow;

        _tray = new TrayIconService(onShow: ShowMainWindow, onQuit: QuitApplication);

        MainAppWindow.Show();

        // Si le raccourci de veille est déjà activé, le recrée avec le
        // chemin actuel de l'exécutable (ex. après une mise à jour
        // installée ailleurs) — voir StarCitizenAutolaunch.RefreshShortcut.
        _ = Task.Run(StarCitizenAutolaunch.RefreshShortcut);
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
