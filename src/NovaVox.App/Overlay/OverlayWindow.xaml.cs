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

/// <summary>Un repère de l'aide-mémoire vaisseaux prêt à afficher dans l'overlay — voir SetShipSheet, ShipCheatSheetPointRowVm côté Réglages.</summary>
public readonly record struct ShipSheetPoint(string Label, string Description, string Color);

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

    // Glisser-déposer des lignes en mode édition (RowDragHandle_*) : ordre
    // courant + élément en cours de déplacement (null hors glisser-déposer).
    private List<string> _rowOrder = OverlayConfig.RowKeys.ToList();
    private string? _draggingKey;
    private FrameworkElement? _draggingHandle;
    private readonly Dictionary<string, Grid> _rowGridByKey;
    private readonly Dictionary<Grid, string> _rowKeyByGrid;

    public OverlayWindow(OverlayConfigStore store)
    {
        InitializeComponent();
        _store = store;

        _rowGridByKey = RowEntries().ToDictionary(r => r.Key, r => r.Row);
        _rowKeyByGrid = _rowGridByKey.ToDictionary(kv => kv.Value, kv => kv.Key);
        foreach (var (_, _, handle, _) in RowEntries())
        {
            handle.MouseMove += RowDragHandle_MouseMove;
            handle.MouseLeftButtonUp += RowDragHandle_MouseLeftButtonUp;
        }

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
        ApplyScale(config.Scale);
        ApplyRowOrder(config.RowOrder);
        _visibleRows = new Dictionary<string, bool>(config.VisibleRows);
        ApplyRowVisibility(_visibleRows);
        RefreshRowVisualsForEditMode();
    }

    /// <summary>
    /// Réordonne les lignes de haut en bas selon <paramref name="order"/> en
    /// déplaçant les Grid déjà existants dans RowsPanel (StackPanel) — pas
    /// de gabarit de données à reconstruire, juste l'ordre des enfants du
    /// panneau qui change, donc tout le reste (bindings, visibilité,
    /// couleurs) reste intact. Toute clé manquante/inconnue est corrigée
    /// silencieusement par OverlayConfig.NormalizeRowOrder plutôt que de
    /// faire disparaître une ligne.
    /// </summary>
    public void ApplyRowOrder(IReadOnlyList<string> order)
    {
        _rowOrder = OverlayConfig.NormalizeRowOrder(order);
        foreach (var key in _rowOrder)
        {
            var element = _rowGridByKey[key];
            RowsPanel.Children.Remove(element);
            RowsPanel.Children.Add(element);
        }
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

    public void ApplyAppearance(string bgColorHex, int bgOpacityPercent, string textColorHex, int textOpacityPercent)
    {
        var bgColor = (Color)ColorConverter.ConvertFromString(bgColorHex)!;
        bgColor.A = (byte)Math.Clamp(bgOpacityPercent * 255 / 100, 0, 255);
        PanelBorder.Background = new SolidColorBrush(bgColor);

        var textColor = (Color)ColorConverter.ConvertFromString(textColorHex)!;
        textColor.A = (byte)Math.Clamp(textOpacityPercent * 255 / 100, 0, 255);
        var textBrush = new SolidColorBrush(textColor);
        foreach (var tb in RowTextBlocks())
            tb.Foreground = textBrush;
    }

    /// <summary>
    /// Met l'overlay entier (texte, icônes, espacements) à l'échelle
    /// <paramref name="scale"/> — voir le ScaleTransform sur le Grid
    /// racine (OverlayWindow.xaml) : un LayoutTransform recalcule aussi la
    /// taille finale de la fenêtre (SizeToContent="WidthAndHeight"), donc
    /// pas de contenu coupé ni de fenêtre restée à sa taille d'origine.
    /// Valeur hors de [OverlayConfig.MinScale, MaxScale] rejetée en
    /// silence (repli sur l'échelle déjà appliquée) plutôt que de risquer
    /// une fenêtre illisible (trop petite) ou hors-écran (trop grande).
    /// </summary>
    public void ApplyScale(double scale)
    {
        if (scale < OverlayConfig.MinScale || scale > OverlayConfig.MaxScale) return;
        OverlayScaleTransform.ScaleX = scale;
        OverlayScaleTransform.ScaleY = scale;
    }

    private IEnumerable<TextBlock> RowTextBlocks()
    {
        yield return TimeValue;
        yield return ListeningLabel; yield return ListeningValue;
        yield return MicLabel; yield return MicValue;
        yield return PhraseValue;
        yield return ZoneLabel; yield return ZoneValue;
        yield return ArmisticeLabel; yield return ArmisticeValue;
        yield return JuridictionLabel; yield return JuridictionValue;
        yield return LastCmdValue;
        yield return ShipSheetTitle;
        // Les lignes de repères (ShipSheetPointsList) sont générées par
        // DataTemplate à chaque SetShipSheet, pas des TextBlock nommés
        // fixes, donc pas dans cette énumération. Leur description garde la
        // couleur neutre #DBE4EE codée en dur dans le gabarit (comme avant),
        // mais le libellé du repère suit désormais SA PROPRE couleur
        // (ShipSheetPoint.Color, réglable indépendamment par repère dans
        // Réglages > 🚀 Vaisseaux) plutôt que la couleur de texte globale de
        // l'overlay — comportement voulu, pas un oubli.
    }

    public void ApplyRowVisibility(Dictionary<string, bool> visibleRows)
    {
        RowTime.Visibility = RowVisibility(visibleRows, "time");
        RowListening.Visibility = RowVisibility(visibleRows, "listening");
        RowMic.Visibility = RowVisibility(visibleRows, "mic");
        RowPhrase.Visibility = RowVisibility(visibleRows, "phrase");
        RowZone.Visibility = RowVisibility(visibleRows, "zone");
        RowArmistice.Visibility = RowVisibility(visibleRows, "armistice");
        RowJuridiction.Visibility = RowVisibility(visibleRows, "juridiction");
        RowLastCmd.Visibility = RowVisibility(visibleRows, "lastCmd");
        RowShipSheet.Visibility = RowVisibility(visibleRows, "shipSheet");
    }

    private static Visibility RowVisibility(Dictionary<string, bool> rows, string key) =>
        !rows.TryGetValue(key, out var visible) || visible ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// Bascule l'affichage d'UNE ligne (ex. "shipSheet") sans passer par le
    /// mode édition de l'overlay (voir RowVisibilityCheckbox_Changed) —
    /// utilisée par la case à cocher dédiée de Réglages > 🚀 Vaisseaux, pour
    /// afficher/masquer l'aide-mémoire vaisseaux dans l'overlay sans avoir
    /// à déverrouiller/reverrouiller. Met à jour _visibleRows (source de
    /// vérité en mémoire de cette fenêtre) et la case à cocher équivalente
    /// du mode édition, si elle est actuellement visible, pour que les deux
    /// entrées point restent cohérentes entre elles. La persistance disque
    /// est laissée à l'appelant (voir MainWindow.ShipSheetOverlayVisibleCheckbox_Changed,
    /// qui passe par AppState.SaveOverlay — même fichier overlay_config.json).
    /// </summary>
    public void SetRowVisible(string key, bool visible)
    {
        _visibleRows[key] = visible;
        ApplyRowVisibility(_visibleRows);
        RefreshRowVisualsForEditMode();
    }

    /// <summary>État actuel (en mémoire) d'une ligne — pour initialiser la case à cocher dédiée de Réglages > 🚀 Vaisseaux à l'ouverture.</summary>
    public bool IsRowVisible(string key) => !_visibleRows.TryGetValue(key, out var visible) || visible;

    private IEnumerable<(Grid Row, CheckBox Checkbox, FrameworkElement DragHandle, string Key)> RowEntries()
    {
        yield return (RowTime, TimeRowCheckbox, TimeDragHandle, "time");
        yield return (RowListening, ListeningRowCheckbox, ListeningDragHandle, "listening");
        yield return (RowMic, MicRowCheckbox, MicDragHandle, "mic");
        yield return (RowPhrase, PhraseRowCheckbox, PhraseDragHandle, "phrase");
        yield return (RowZone, ZoneRowCheckbox, ZoneDragHandle, "zone");
        yield return (RowArmistice, ArmisticeRowCheckbox, ArmisticeDragHandle, "armistice");
        yield return (RowJuridiction, JuridictionRowCheckbox, JuridictionDragHandle, "juridiction");
        yield return (RowLastCmd, LastCmdRowCheckbox, LastCmdDragHandle, "lastCmd");
        yield return (RowShipSheet, ShipSheetRowCheckbox, ShipSheetDragHandle, "shipSheet");
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
            foreach (var (row, checkbox, dragHandle, key) in RowEntries())
            {
                var visible = !_visibleRows.TryGetValue(key, out var v) || v;
                if (_editMode)
                {
                    row.Visibility = Visibility.Visible;
                    row.Opacity = visible ? 1.0 : 0.35;
                    checkbox.Visibility = Visibility.Visible;
                    checkbox.IsChecked = visible;
                    dragHandle.Visibility = Visibility.Visible;
                }
                else
                {
                    row.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                    row.Opacity = 1.0;
                    checkbox.Visibility = Visibility.Collapsed;
                    dragHandle.Visibility = Visibility.Collapsed;
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
        foreach (var (row, _, _, rowKey) in RowEntries())
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

    private bool _micActive = true;
    private bool _listeningActive = true;

    public void SetListening(bool active)
    {
        _listeningActive = active;
        ListeningValue.Text = active ? "En cours" : "Arrêtée";
        RefreshBorderColor();
    }

    public void SetMicActive(bool active)
    {
        _micActive = active;
        MicValue.Text = active ? "Actif" : "Coupé";
        RefreshBorderColor();
    }

    public void SetPhrase(string? text) => PhraseValue.Text = string.IsNullOrEmpty(text) ? "…" : text;
    public void SetZone(string? text) => ZoneValue.Text = string.IsNullOrEmpty(text) ? "—" : text;
    public void SetJuridiction(string? text) => JuridictionValue.Text = string.IsNullOrEmpty(text) ? "—" : text;

    /// <summary>
    /// Zone d'armistice (combat interdit) — voir ArmisticeEnteredRegex/
    /// ArmisticeLeftRegex (GameLogAnnouncer.cs). L'icône bascule en plus du
    /// texte, pour rester repérable d'un coup d'œil comme le symbole du HUD
    /// natif du jeu (voir la réponse donnée sur l'origine de ce symbole :
    /// Unicode générique, pas une icône extraite du jeu).
    /// </summary>
    public void SetArmistice(bool active)
    {
        ArmisticeIcon.Text = active ? "🚫" : "⚔";
        ArmisticeValue.Text = active ? "Oui" : "Non";
    }
    public void SetLastCommand(string? text) => LastCmdValue.Text = string.IsNullOrEmpty(text) ? "—" : text;

    /// <summary>
    /// Aide-mémoire vaisseau (Réglages > 🚀 Vaisseaux, AiConfig.ShipCheatSheets/
    /// ActiveShipCheatSheet) : affiche/masque restent décidés comme les
    /// autres lignes par ApplyRowVisibility/RefreshRowVisualsForEditMode
    /// (config "shipSheet") — ce point ne fait que remplir le contenu, sans
    /// jamais toucher à la visibilité de RowShipSheet.
    /// </summary>
    public void SetShipSheet(string? shipName, IReadOnlyList<ShipSheetPoint> points)
    {
        ShipSheetTitle.Text = string.IsNullOrEmpty(shipName) ? "Aucun vaisseau sélectionné" : shipName;
        ShipSheetPointsList.ItemsSource = points;
    }

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
        RefreshBorderColor();
        if (_hwnd != IntPtr.Zero) WindowClickThrough.SetClickThrough(_hwnd, clickThrough: !editable);
        RefreshRowVisualsForEditMode();
    }

    /// <summary>
    /// Cadre rouge (pas tout le panneau, pour rester lisible) tant que le
    /// micro est coupé OU que l'écoute n'est pas active — remplace l'ancien
    /// fond plein rouge (SetMicActive) qui rendait le texte illisible sur
    /// certaines couleurs de texte. Priorité sur la couleur cyan d'édition :
    /// l'alerte doit rester visible même overlay déverrouillé.
    /// </summary>
    private void RefreshBorderColor()
    {
        if (!_micActive || !_listeningActive)
        {
            PanelBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0x33, 0x33));
            PanelBorder.BorderThickness = new Thickness(2);
        }
        else
        {
            PanelBorder.BorderBrush = _editMode
                ? new SolidColorBrush(Color.FromRgb(0x2D, 0xD4, 0xFF))
                : new SolidColorBrush(Color.FromArgb(0x59, 0x2D, 0xD4, 0xFF));
            PanelBorder.BorderThickness = new Thickness(1);
        }
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_editMode) return;
        // Ne jamais démarrer un DragMove() à partir d'un clic sur une case à
        // cocher : contrairement à une première tentative, ce filtrage ne
        // doit PAS passer par e.Handled côté CheckBox (Preview...Down) — la
        // case partage l'EventArgs entre son passage tunnel et bulle, donc
        // marquer Handled=true dès la phase Preview empêche aussi la
        // CheckBox de traiter son propre clic (plus de Checked/Unchecked du
        // tout). Un filtrage par contenu (élément d'origine) ici, uniquement
        // côté fenêtre, évite ce problème.
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

    /// <summary>
    /// Démarre le glisser d'une ligne — capture la souris sur LA POIGNÉE
    /// (pas la fenêtre), et marque l'évènement traité pour empêcher
    /// OnMouseLeftButtonDown de démarrer un DragMove() de toute la fenêtre
    /// à la place (le traitement en phase Preview, avant que l'évènement ne
    /// remonte jusqu'à la fenêtre, suffit — pas besoin d'un filtrage par
    /// contenu comme IsWithinCheckbox ci-dessus). La capture garantit que
    /// MouseMove/MouseLeftButtonUp continuent d'arriver à cette poignée même
    /// une fois le curseur sorti de ses 16px de large.
    /// </summary>
    private void RowDragHandle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_editMode) return;
        if (sender is not FrameworkElement { Tag: string key } handle) return;
        _draggingKey = key;
        _draggingHandle = handle;
        handle.CaptureMouse();
        e.Handled = true;
    }

    private void RowDragHandle_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggingKey is null || e.LeftButton != MouseButtonState.Pressed) return;
        var position = e.GetPosition(RowsPanel);
        MoveRowToIndex(_draggingKey, RowIndexAtY(position.Y));
    }

    private void RowDragHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggingKey is null) return;
        _draggingHandle?.ReleaseMouseCapture();
        _draggingHandle = null;
        _draggingKey = null;
        e.Handled = true;

        try
        {
            _store.Save(enabled: true, rowOrder: _rowOrder);
        }
        catch (Exception ex)
        {
            AppLog.Append(NovaVoxPaths.BaseDirectory, $"[Overlay] Sauvegarde de l'ordre des lignes échouée ({ex.Message}).", "diagnostic");
        }
    }

    /// <summary>
    /// Trouve la position "insérer avant la ligne i" (dans RowsPanel.Children,
    /// ordre actuel) sous la position verticale <paramref name="y"/> —
    /// bascule au MILIEU de chaque ligne survolée plutôt qu'à son bord, plus
    /// naturel au glisser. Retourne RowsPanel.Children.Count (au-delà de la
    /// dernière ligne) si y dépasse tout le contenu, pour permettre de
    /// déposer une ligne tout en bas — jamais coincée à l'avant-dernière
    /// position (voir MoveRowToIndex pour la compensation de décalage).
    /// </summary>
    private int RowIndexAtY(double y)
    {
        double cursor = 0;
        for (var i = 0; i < RowsPanel.Children.Count; i++)
        {
            if (RowsPanel.Children[i] is not FrameworkElement fe) continue;
            var height = fe.ActualHeight + fe.Margin.Top + fe.Margin.Bottom;
            if (y < cursor + height / 2) return i;
            cursor += height;
        }
        return RowsPanel.Children.Count;
    }

    /// <summary>
    /// Déplace la ligne <paramref name="key"/> pour qu'elle se retrouve juste
    /// avant l'index <paramref name="targetIndex"/> (voir RowIndexAtY) DANS
    /// L'ORDRE ACTUEL (avant retrait) — un retrait à un index inférieur à
    /// targetIndex décale tout ce qui suit d'un cran, d'où la compensation
    /// (targetIndex--) uniquement quand on déplace vers le bas, sinon
    /// l'élément atterrit systématiquement une case trop loin.
    /// </summary>
    private void MoveRowToIndex(string key, int targetIndex)
    {
        var element = _rowGridByKey[key];
        var currentIndex = RowsPanel.Children.IndexOf(element);
        if (currentIndex < 0) return;

        targetIndex = Math.Clamp(targetIndex, 0, RowsPanel.Children.Count);
        if (currentIndex < targetIndex) targetIndex--;
        if (targetIndex == currentIndex) return;

        RowsPanel.Children.RemoveAt(currentIndex);
        RowsPanel.Children.Insert(Math.Clamp(targetIndex, 0, RowsPanel.Children.Count), element);
        _rowOrder = RowsPanel.Children.OfType<Grid>().Select(g => _rowKeyByGrid[g]).ToList();
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
