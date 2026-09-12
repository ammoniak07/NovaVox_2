using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
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
        var itemTemplate = BuildOpaqueMenuItemTemplate();

        var showItem = new MenuItem { Header = "Afficher NOVAVOX", FontWeight = FontWeights.Bold, Template = itemTemplate };
        showItem.Click += (_, _) => onShow();
        menu.Items.Add(showItem);

        menu.Items.Add(new Separator());

        var quitItem = new MenuItem { Header = "Quitter", Template = itemTemplate };
        quitItem.Click += (_, _) => onQuit();
        menu.Items.Add(quitItem);

        _icon.ContextMenu = menu;
        _icon.TrayMouseDoubleClick += (_, _) => onShow();
    }

    /// <summary>
    /// Un simple Foreground/Background en valeur locale (priorité maximale
    /// en WPF) n'a pas suffi à rendre ce menu lisible en conditions
    /// réelles : signe que le vrai coupable est probablement une opacité
    /// réduite appliquée par un déclencheur du thème ambiant (état
    /// "inactif"/désactivé du nouveau thème Fluent par défaut de .NET 8),
    /// propriété qu'aucun Foreground/Background ne peut contrer. Ce
    /// template dédié remplace entièrement le rendu de l'élément — plus
    /// aucun déclencheur du thème d'origine ne s'applique, donc plus rien
    /// ne peut discrètement réduire l'opacité du texte.
    /// </summary>
    private static ControlTemplate BuildOpaqueMenuItemTemplate()
    {
        const string xaml = """
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                              xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                              TargetType="MenuItem">
                <Border x:Name="Bg" Background="White" Padding="16,7">
                    <TextBlock Text="{TemplateBinding Header}" Foreground="Black" FontWeight="{TemplateBinding FontWeight}" />
                </Border>
                <ControlTemplate.Triggers>
                    <Trigger Property="IsHighlighted" Value="True">
                        <Setter TargetName="Bg" Property="Background" Value="#CCE4F7" />
                    </Trigger>
                </ControlTemplate.Triggers>
            </ControlTemplate>
            """;
        return (ControlTemplate)XamlReader.Parse(xaml);
    }

    public void Dispose() => _icon.Dispose();
}
