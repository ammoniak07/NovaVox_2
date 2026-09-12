using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Hardcodet.Wpf.TaskbarNotification;
using NovaVox.Core;

namespace NovaVox.App.Tray;

/// <summary>
/// Icône NOVAVOX dans la barre des tâches Windows — port de
/// _start_system_tray (app.py). Le menu "Profil de commandes" (soumenu
/// dynamique, cases radio sur le profil actif) sera branché une fois
/// l'état applicatif des profils disponible côté WPF (voir tâche #11) ;
/// pour l'instant seuls Afficher/Quitter sont câblés.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly TaskbarIcon _icon;

    public TrayIconService(Action onShow, Action onQuit)
    {
        _icon = new TaskbarIcon { ToolTipText = "NovaVox" };

        var iconPath = Path.Combine(NovaVoxPaths.BaseDirectory, "icon.ico");
        if (File.Exists(iconPath))
        {
            _icon.Icon = new Icon(iconPath);
        }

        var menu = new ContextMenu();
        // Le thème de l'appli définit un style TextBlock implicite (texte
        // clair, pensé pour ses propres fenêtres au fond sombre) qui
        // s'applique globalement, y compris ici : ce menu contextuel du
        // system tray est un popup Windows classique à fond clair. Le
        // header d'un MenuItem n'est pas forcément rendu via un TextBlock
        // (WPF y substitue un AccessText pour gérer le raccourci clavier
        // souligné), donc masquer le style TextBlock seul ne suffisait pas
        // partout (ex: "Quitter" restait illisible) — Foreground=Black en
        // valeur locale directement sur chaque MenuItem, priorité maximale
        // dans WPF, au-dessus de tout style/trigger, règle le problème
        // dans tous les cas.
        menu.Resources.Add(typeof(TextBlock), new Style(typeof(TextBlock)));

        var showItem = new MenuItem { Header = "Afficher NOVAVOX", FontWeight = FontWeights.Bold, Foreground = Brushes.Black };
        showItem.Click += (_, _) => onShow();
        menu.Items.Add(showItem);

        menu.Items.Add(new Separator());

        var quitItem = new MenuItem { Header = "Quitter", Foreground = Brushes.Black };
        quitItem.Click += (_, _) => onQuit();
        menu.Items.Add(quitItem);

        _icon.ContextMenu = menu;
        _icon.TrayMouseDoubleClick += (_, _) => onShow();
    }

    public void Dispose() => _icon.Dispose();
}
