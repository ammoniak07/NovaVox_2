using System.Drawing;
using System.Windows;
using System.Windows.Controls;
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

        var showItem = new MenuItem { Header = "Afficher NOVAVOX", FontWeight = FontWeights.Bold };
        showItem.Click += (_, _) => onShow();
        menu.Items.Add(showItem);

        menu.Items.Add(new Separator());

        var quitItem = new MenuItem { Header = "Quitter" };
        quitItem.Click += (_, _) => onQuit();
        menu.Items.Add(quitItem);

        _icon.ContextMenu = menu;
        _icon.TrayMouseDoubleClick += (_, _) => onShow();
    }

    public void Dispose() => _icon.Dispose();
}
