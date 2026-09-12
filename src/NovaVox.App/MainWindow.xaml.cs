using System.ComponentModel;
using System.Windows;
using NovaVox.Core;
using NovaVox.Core.Config;

namespace NovaVox.App;

public partial class MainWindow : Window
{
    private readonly WindowConfigStore _windowConfigStore = new(NovaVoxPaths.BaseDirectory);
    private bool _restoring = true;

    /// <summary>
    /// Mis à true uniquement par le "Quitter" du menu tray (voir
    /// App.QuitApplication) : sans ça, le bouton X de la fenêtre masque
    /// juste la fenêtre au lieu de fermer l'application — port du
    /// comportement tray_state de _wire_main_window_events (app.py).
    /// </summary>
    public bool IsQuitting { get; set; }

    public MainWindow()
    {
        InitializeComponent();
        MinWidth = WindowConfigStore.MinSize.W;
        MinHeight = WindowConfigStore.MinSize.H;

        Loaded += OnLoaded;
        Closing += OnClosing;
        SizeChanged += (_, _) => SaveWindowConfig();
        LocationChanged += (_, _) => SaveWindowConfig();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var bounds = _windowConfigStore.Load(VirtualScreenBounds());
        Width = bounds.Width;
        Height = bounds.Height;
        if (bounds.X is not null && bounds.Y is not null)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = bounds.X.Value;
            Top = bounds.Y.Value;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        _restoring = false;
    }

    private static (int X, int Y, int W, int H)? VirtualScreenBounds()
    {
        try
        {
            return (
                (int)SystemParameters.VirtualScreenLeft,
                (int)SystemParameters.VirtualScreenTop,
                (int)SystemParameters.VirtualScreenWidth,
                (int)SystemParameters.VirtualScreenHeight);
        }
        catch
        {
            return null;
        }
    }

    private void SaveWindowConfig()
    {
        // Sauvegarde redondante à chaque redimensionnement/déplacement
        // plutôt que de dépendre uniquement de la fermeture (voir
        // _on_resized/_on_moved, app.py) : la dernière taille/position
        // connue reste sur le disque même si "Closing" ne se déclenche
        // pas de façon fiable. Ignorée pendant la restauration initiale
        // pour ne pas réécrire une valeur transitoire.
        if (_restoring || WindowState != WindowState.Normal) return;
        try
        {
            _windowConfigStore.Save((int)Width, (int)Height, (int)Left, (int)Top);
        }
        catch
        {
            // Confort seulement, jamais bloquant.
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        SaveWindowConfig();
        if (!IsQuitting)
        {
            // Icône tray présente : la croix masque la fenêtre au lieu de
            // fermer l'application (voir _on_closing, app.py).
            e.Cancel = true;
            Hide();
        }
    }
}
