using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using NovaVox.App.Autolaunch;
using NovaVox.App.Tray;
using NovaVox.Core;

namespace NovaVox.App;

public partial class App : Application
{
    private TrayIconService? _tray;

    public MainWindow? MainAppWindow { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Toute exception qui échapperait autrement complètement (l'appli
        // disparaît sans aucune trace) est journalisée en dernier recours
        // dans Log/ avant que le processus ne se termine — ne cherche pas
        // à "récupérer" ni continuer dans un état incertain, juste à
        // laisser une trace exploitable.
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            AppLog.AppendException(NovaVoxPaths.BaseDirectory, "Exception non gérée (AppDomain)", args.ExceptionObject as Exception ?? new Exception(args.ExceptionObject?.ToString() ?? "inconnue"));
        DispatcherUnhandledException += (_, args) =>
            AppLog.AppendException(NovaVoxPaths.BaseDirectory, "Exception non gérée (thread IU)", args.Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLog.AppendException(NovaVoxPaths.BaseDirectory, "Exception de tâche jamais observée", args.Exception);
            args.SetObserved();
        };

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
