using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using NovaVox.Core;
using NovaVox.Core.Config;

namespace NovaVox.App.Overlay;

/// <summary>
/// Overlay affiché par-dessus Star Citizen (état du micro, dernière
/// phrase reconnue, zone) — port de la fenêtre pywebview overlay.html +
/// _create_overlay_window (app.py), en WPF NATIF : AllowsTransparency
/// suffit ici pour un fond réellement transparent (fenêtre ordinaire,
/// pas d'injection dans le jeu — voir le commentaire au-dessus de
/// load_overlay_config côté Python pour le rappel de cette limite
/// volontaire). Le clic-traversant (mode "verrouillé") est obtenu en
/// ajoutant WS_EX_TRANSPARENT à la fenêtre déjà créée, exactement comme
/// _set_window_clickthrough.
/// </summary>
public partial class OverlayWindow : Window
{
    private readonly OverlayConfigStore _store;
    private readonly DispatcherTimer _clockTimer;
    private bool _editMode;
    private IntPtr _hwnd;
    private Dictionary<string, bool> _visibleRows = new();
    // Évite que le rechargement programmatique des cases à cocher (IsChecked
    // remis à jour depuis _visibleRows dans RefreshRowVisualsForEditMode) ne
    // déclenche à son tour RowVisibilityCheckbox_Changed, qui réécrirait le
    // fichier de config avec les mêmes valeurs à chaque bascule de mode.
    private bool _suppressCheckboxEvents;

    public OverlayWindow(OverlayConfigStore store)
    {
        InitializeComponent();
        _store = store;

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            SetEditMode(false); // clic-traversant par défaut (mode "verrouillé")
        };
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        Closing += OnClosing;

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();
        UpdateClock();
    }

    public void LoadFromConfig()
    {
        var config = _store.Load();
        if (config.X is not null && config.Y is not null && IsValidScreenPosition(config.X.Value, config.Y.Value))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = config.X.Value;
            Top = config.Y.Value;
        }
        ApplyAppearance(config.BgColor, config.BgOpacity, config.TextColor, config.TextOpacity);
        _visibleRows = new Dictionary<string, bool>(config.VisibleRows);
        ApplyRowVisibility(_visibleRows);
        RefreshRowVisualsForEditMode();
    }

    /// <summary>
    /// Rejette une position enregistrée aberrante (ex: -26214, constaté en
    /// conditions réelles — laisse Windows replacer la fenêtre par défaut)
    /// plutôt que de rendre l'overlay invisible car placé hors de tout écran.
    /// La marge tolère qu'une fenêtre déjà positionnée déborde légèrement
    /// (bord d'écran, changement de résolution).
    /// </summary>
    private static bool IsValidScreenPosition(double x, double y)
    {
        const double margin = 500;
        return double.IsFinite(x) && double.IsFinite(y)
            && x >= SystemParameters.VirtualScreenLeft - margin
            && x <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth + margin
            && y >= SystemParameters.VirtualScreenTop - margin
            && y <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight + margin;
    }

    private Brush? _normalPanelBackground;

    public void ApplyAppearance(string bgColorHex, int bgOpacityPercent, string textColorHex, int textOpacityPercent)
    {
        var bgColor = (Color)ColorConverter.ConvertFromString(bgColorHex)!;
        bgColor.A = (byte)Math.Clamp(bgOpacityPercent * 255 / 100, 0, 255);
        _normalPanelBackground = new SolidColorBrush(bgColor);
        PanelBorder.Background = _normalPanelBackground;

        var textColor = (Color)ColorConverter.ConvertFromString(textColorHex)!;
        textColor.A = (byte)Math.Clamp(textOpacityPercent * 255 / 100, 0, 255);
        var textBrush = new SolidColorBrush(textColor);
        foreach (var tb in RowTextBlocks())
            tb.Foreground = textBrush;
    }

    private IEnumerable<TextBlock> RowTextBlocks()
    {
        yield return TimeValue;
        yield return ListeningLabel; yield return ListeningValue;
        yield return MicLabel; yield return MicValue;
        yield return PhraseValue;
        yield return ZoneLabel; yield return ZoneValue;
        yield return LastCmdValue;
    }

    public void ApplyRowVisibility(Dictionary<string, bool> visibleRows)
    {
        RowTime.Visibility = RowVisibility(visibleRows, "time");
        RowListening.Visibility = RowVisibility(visibleRows, "listening");
        RowMic.Visibility = RowVisibility(visibleRows, "mic");
        RowPhrase.Visibility = RowVisibility(visibleRows, "phrase");
        RowZone.Visibility = RowVisibility(visibleRows, "zone");
        RowLastCmd.Visibility = RowVisibility(visibleRows, "lastCmd");
    }

    private static Visibility RowVisibility(Dictionary<string, bool> rows, string key) =>
        !rows.TryGetValue(key, out var visible) || visible ? Visibility.Visible : Visibility.Collapsed;

    private IEnumerable<(Grid Row, CheckBox Checkbox, string Key)> RowEntries()
    {
        yield return (RowTime, TimeRowCheckbox, "time");
        yield return (RowListening, ListeningRowCheckbox, "listening");
        yield return (RowMic, MicRowCheckbox, "mic");
        yield return (RowPhrase, PhraseRowCheckbox, "phrase");
        yield return (RowZone, ZoneRowCheckbox, "zone");
        yield return (RowLastCmd, LastCmdRowCheckbox, "lastCmd");
    }

    /// <summary>
    /// En mode édition, toutes les lignes redeviennent visibles (atténuées si
    /// masquées dans la config, pour rester cliquables et réactivables) et
    /// leurs cases à cocher apparaissent ; hors édition, seule la config
    /// (ApplyRowVisibility) décide de ce qui s'affiche et les cases
    /// disparaissent. IsChecked est réassigné sans passer par l'utilisateur :
    /// _suppressCheckboxEvents évite une écriture de config en boucle.
    /// </summary>
    private void RefreshRowVisualsForEditMode()
    {
        _suppressCheckboxEvents = true;
        try
        {
            foreach (var (row, checkbox, key) in RowEntries())
            {
                var visible = !_visibleRows.TryGetValue(key, out var v) || v;
                if (_editMode)
                {
                    row.Visibility = Visibility.Visible;
                    row.Opacity = visible ? 1.0 : 0.35;
                    checkbox.Visibility = Visibility.Visible;
                    checkbox.IsChecked = visible;
                }
                else
                {
                    row.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                    row.Opacity = 1.0;
                    checkbox.Visibility = Visibility.Collapsed;
                }
            }
        }
        finally
        {
            _suppressCheckboxEvents = false;
        }
    }

    private void RowVisibilityCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressCheckboxEvents) return;
        if (sender is not CheckBox { Tag: string key } checkbox) return;

        var visible = checkbox.IsChecked == true;
        _visibleRows[key] = visible;
        foreach (var (row, _, rowKey) in RowEntries())
        {
            if (rowKey == key)
            {
                row.Opacity = visible ? 1.0 : 0.35;
                break;
            }
        }

        // Log volontairement inconditionnel (pas seulement en cas d'échec) :
        // repère de diagnostic pour confirmer que le clic atteint bien la
        // case (si cette ligne n'apparaît jamais malgré des clics, le
        // problème est en amont — le clic ne parvient pas jusqu'à la
        // CheckBox, ex. capturé par un DragMove()).
        AppLog.Append(NovaVoxPaths.BaseDirectory, $"[Overlay] Case '{key}' -> {(visible ? "cochée" : "décochée")}.", "diagnostic");

        try
        {
            _store.Save(enabled: true, visibleRows: _visibleRows);
        }
        catch (Exception ex)
        {
            AppLog.Append(NovaVoxPaths.BaseDirectory, $"[Overlay] Sauvegarde des lignes affichées échouée ({ex.Message}).", "diagnostic");
        }
    }

    /// <summary>Empêche le clic sur une case à cocher de déclencher DragMove() (voir OnMouseLeftButtonDown), qui capturerait la souris avant que la case ne réagisse au clic.</summary>
    private void StopDragOnCheckbox(object sender, MouseButtonEventArgs e) => e.Handled = true;

    public void SetListening(bool active) => ListeningValue.Text = active ? "En cours" : "Arrêtée";

    public void SetMicActive(bool active)
    {
        MicValue.Text = active ? "Actif" : "Coupé";
        // Fond rouge dédié pendant que le micro est coupé (mic-cut,
        // overlay.html) — indépendant du flash de commande (calque séparé).
        // Restaure le fond normal (mémorisé par ApplyAppearance) plutôt que
        // de se réassigner à lui-même : sinon, une fois rouge, il le reste
        // pour toujours même quand le micro se réactive.
        PanelBorder.Background = active
            ? _normalPanelBackground ?? PanelBorder.Background
            : new SolidColorBrush(Color.FromArgb(0xB8, 0x78, 0x14, 0x14));
    }

    public void SetPhrase(string? text) => PhraseValue.Text = string.IsNullOrEmpty(text) ? "…" : text;
    public void SetZone(string? text) => ZoneValue.Text = string.IsNullOrEmpty(text) ? "—" : text;
    public void SetLastCommand(string? text) => LastCmdValue.Text = string.IsNullOrEmpty(text) ? "—" : text;

    public void UpdateClock() => TimeValue.Text = DateTime.Now.ToString("HH:mm:ss");

    /// <summary>Flash vert (2s) à chaque commande déclenchée — voir #panel.cmd-flash (overlay.html).</summary>
    public void FlashCommand()
    {
        var animation = new DoubleAnimation(0.85, 0.0, TimeSpan.FromSeconds(2))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        FlashOverlay.BeginAnimation(OpacityProperty, animation);
    }

    /// <summary>
    /// Mode "déplacer" (édition, bordure cyan, glisser à la souris) vs
    /// mode "verrouillé" (clic-traversant, passif) — bouton "Déplacer
    /// l'overlay" des réglages, voir overlay_set_edit_mode côté Python.
    /// </summary>
    public void SetEditMode(bool editable)
    {
        _editMode = editable;
        PanelBorder.BorderBrush = editable
            ? new SolidColorBrush(Color.FromRgb(0x2D, 0xD4, 0xFF))
            : new SolidColorBrush(Color.FromArgb(0x59, 0x2D, 0xD4, 0xFF));
        if (_hwnd != IntPtr.Zero) WindowClickThrough.SetClickThrough(_hwnd, clickThrough: !editable);
        RefreshRowVisualsForEditMode();
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_editMode) return;
        // Filet de sécurité en plus de StopDragOnCheckbox (qui marque déjà
        // l'évènement Handled) : même si un clic sur la case à cocher
        // parvenait quand même jusqu'ici, ne jamais démarrer un DragMove()
        // à partir d'elle — sinon la case ne peut plus être (dé)cochée,
        // le déplacement de la fenêtre "avalant" le clic.
        if (IsWithinCheckbox(e.OriginalSource as DependencyObject)) return;
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // Le bouton a déjà été relâché avant l'appel : sans conséquence.
        }
    }

    private static bool IsWithinCheckbox(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is CheckBox) return true;
            source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        }
        return false;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _clockTimer.Stop();
        try
        {
            if (IsValidScreenPosition(Left, Top))
                _store.Save(enabled: true, x: (int)Left, y: (int)Top);
            else
                _store.Save(enabled: true); // position aberrante : ne pas la persister, la fenêtre se replacera par défaut au prochain lancement
        }
        catch (Exception ex)
        {
            AppLog.Append(NovaVoxPaths.BaseDirectory, $"[Overlay] Sauvegarde de la position/apparence à la fermeture échouée ({ex.Message}).", "diagnostic");
        }
    }
}
