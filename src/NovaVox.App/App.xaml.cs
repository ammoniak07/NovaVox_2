using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using NovaVox.App.Autolaunch;
using NovaVox.App.Tray;
using NovaVox.Core;
using NovaVox.Core.Update;

namespace NovaVox.App;

public partial class App : Application
{
    private TrayIconService? _tray;

    // Noms uniques à cette édition .NET (même GUID que AppId dans
    // installer.iss) : une éventuelle instance V1 (Python) reste
    // indépendante, ce garde-fou ne concerne que NovaVox V2.
    private const string SingleInstanceMutexName = "NovaVoxNET_SingleInstance_7B2E9F41-3C8A-4D6E-9F12-1A5C6D8E3B7F";
    private const string ShowWindowEventName = "NovaVoxNET_ShowWindow_7B2E9F41-3C8A-4D6E-9F12-1A5C6D8E3B7F";
    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _showWindowEvent;

    public MainWindow? MainAppWindow { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Garde-fou mono-instance : sans ça, rien n'empêche deux
        // NovaVox.exe de tourner en même temps (ex. lancé à la main
        // pendant que le raccourci de veille "--wait-for-sc" attend déjà
        // en arrière-plan) — deux captures micro concurrentes, deux
        // icônes tray, et fermer l'une des deux ne touche pas l'autre.
        // Vérifié tout en tout début, avant le moindre travail (log,
        // attente Star Citizen, fenêtre...).
        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            // Une instance tourne déjà (fenêtre visible, masquée dans le
            // tray, ou en veille silencieuse) : la signale pour qu'elle
            // s'affiche au premier plan plutôt que de démarrer un second
            // processus concurrent.
            try
            {
                using var showEvent = EventWaitHandle.OpenExisting(ShowWindowEventName);
                showEvent.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                // L'autre instance n'a pas encore créé son événement (tout
                // début de son propre démarrage) : rien à signaler, elle
                // affichera de toute façon sa fenêtre d'elle-même sous peu.
            }
            Shutdown();
            return;
        }

        // Écoute en tâche de fond (thread dédié, pas le thread IU) les
        // demandes d'affichage envoyées par une tentative de second
        // lancement. BeginInvoke est mis en file d'attente sur le
        // Dispatcher même s'il n'a pas encore démarré sa boucle de
        // messages (tout début du lancement, avant que MainAppWindow soit
        // construite un peu plus bas) : il s'exécute dès qu'elle démarre.
        _showWindowEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowWindowEventName);
        new Thread(() =>
        {
            while (true)
            {
                _showWindowEvent.WaitOne();
                Dispatcher.BeginInvoke(ShowMainWindow);
            }
        })
        { IsBackground = true }.Start();

        var version = VersionUtil.GetAppVersion(Path.Combine(NovaVoxPaths.BaseDirectory, "patch_maj.txt"));
        AppLog.AppendStartupBanner(NovaVoxPaths.BaseDirectory, version);
        AppLog.PruneOldLogs(NovaVoxPaths.BaseDirectory);

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

        MainAppWindow = new MainWindow();
        MainWindow = MainAppWindow;

        // Toujours créée tout de suite, même en veille (--wait-for-sc,
        // ci-dessous) : sans ça, un lancement au démarrage de Windows
        // restait un processus totalement invisible (ni fenêtre, ni icône
        // tray) pendant potentiellement toute la session, tant que Star
        // Citizen n'était pas lancé — impossible à ouvrir ou même à
        // repérer autrement qu'en tuant le processus dans le Gestionnaire
        // des tâches (voir remontée utilisateur). L'icône seule permet
        // maintenant de quitter ou de forcer l'affichage à tout moment.
        _tray = new TrayIconService(onShow: ShowMainWindow, onQuit: QuitApplication);

        if (e.Args.Contains("--wait-for-sc"))
        {
            // Reste en veille (icône tray visible, fenêtre masquée) jusqu'à
            // détecter Star Citizen — sur un fil DÉDIÉ plutôt que de
            // bloquer OnStartup comme avant : bloquer ici geler aussi la
            // boucle de messages WPF avant même qu'elle démarre, donc ni
            // l'icône tray ni un second lancement demandant l'affichage
            // (voir le fil d'écoute juste au-dessus) ne pouvaient réagir
            // tant que Star Citizen n'avait pas démarré.
            Task.Run(() =>
            {
                StarCitizenAutolaunch.WaitForStarCitizenIfRequested(e.Args);
                Dispatcher.BeginInvoke(ShowMainWindow);
            });
        }
        else
        {
            MainAppWindow.Show();
        }

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
        // ReleaseMutex avant Dispose : créé avec initiallyOwned=true, donc
        // possédé par ce thread — Windows le libérerait de toute façon à
        // la fin du process, mais explicite plutôt qu'implicite ici.
        try { _singleInstanceMutex?.ReleaseMutex(); } catch { /* déjà relâché/non possédé : sans conséquence */ }
        _singleInstanceMutex?.Dispose();
        _showWindowEvent?.Dispose();
        base.OnExit(e);
    }
}
