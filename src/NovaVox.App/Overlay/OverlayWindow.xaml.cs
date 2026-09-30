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
    // GLOBAL courant (toutes colonnes confondues, sert à retrouver l'ordre
    // relatif DANS une colonne — voir OverlayConfig.RowOrder), colonne et
    // FENÊTRE (0 = principale, sinon un satellite détaché — voir
    // OverlayConfig.RowWindow) de chaque ligne, + élément en cours de
    // déplacement (null hors glisser).
    private List<string> _rowOrder = OverlayConfig.RowKeys.ToList();
    private Dictionary<string, int> _rowColumn = OverlayConfig.RowKeys.ToDictionary(k => k, _ => 0);
    private Dictionary<string, int> _rowWindow = OverlayConfig.RowKeys.ToDictionary(k => k, _ => 0);
    private string? _draggingKey;
    private FrameworkElement? _draggingHandle;
    private readonly Dictionary<string, Grid> _rowGridByKey;
    private readonly Dictionary<Grid, string> _rowKeyByGrid;
    private Panel[] ColumnPanels => new Panel[] { Column0, Column1, Column2, Column3, Column4, Column5, Column6, Column7, Column8 };

    /// <summary>Une fenêtre connue (principale ou satellite) pour la logique de glisser-déposer, indépendamment de son type concret.</summary>
    private sealed record OverlayWindowEntry(Window Window, Panel ColumnsPanel, Panel[] Columns);

    // Toutes les fenêtres connues (0 = cette instance elle-même, sinon un
    // satellite créé par DetachRowToNewWindow) — _windows sert au
    // glisser-déposer générique (géométrie), _satellites uniquement aux
    // appels spécifiques (apparence, clic-traversant) qui n'existent que sur
    // OverlaySatelliteWindow. Dernière apparence/échelle appliquée mémorisée
    // pour qu'un satellite créé EN COURS DE SESSION (glisser une ligne hors
    // de l'overlay) reçoive immédiatement le même rendu que la fenêtre
    // principale, sans dépendre de l'ordre d'appel avec ApplyAppearance/
    // ApplyScale (voir CreateSatelliteWindow).
    private readonly Dictionary<int, OverlayWindowEntry> _windows = new();
    private readonly Dictionary<int, OverlaySatelliteWindow> _satellites = new();
    private string _lastBgColorHex = OverlayConfig.DefaultBgColor;
    private int _lastBgOpacity = OverlayConfig.DefaultBgOpacity;
    private double _lastScale = OverlayConfig.DefaultScale;

    public OverlayWindow(OverlayConfigStore store)
    {
        InitializeComponent();
        _store = store;

        _rowGridByKey = RowEntries().ToDictionary(r => r.Key, r => r.Row);
        _rowKeyByGrid = _rowGridByKey.ToDictionary(kv => kv.Value, kv => kv.Key);
        _windows[0] = new OverlayWindowEntry(this, ColumnsPanel, ColumnPanels);
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

    /// <summary>
    /// Affiche/masque l'overlay ET tous ses satellites actuels ensemble — un
    /// satellite ne doit jamais rester visible (ou caché) indépendamment de
    /// la case "Activer l'overlay" qui pilote cette fenêtre principale.
    /// `new` (et non `override`) : Window.Show/Hide ne sont pas virtuelles ;
    /// fonctionne car MainWindow détient _overlayWindow typé OverlayWindow
    /// (pas Window), donc la résolution statique choisit bien ces surcharges.
    /// </summary>
    public new void Show()
    {
        base.Show();
        foreach (var satellite in _satellites.Values) satellite.Show();
    }

    public new void Hide()
    {
        base.Hide();
        foreach (var satellite in _satellites.Values) satellite.Hide();
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
        ApplyLayout(config.RowOrder, config.RowColumns, config.RowWindow, config.SatelliteWindows);
        _visibleRows = new Dictionary<string, bool>(config.VisibleRows);
        ApplyRowVisibility(_visibleRows);
        RefreshRowVisualsForEditMode();
    }

    /// <summary>
    /// Répartit les lignes dans leurs fenêtres (<paramref name="rowWindow"/>,
    /// 0 = principale) puis colonnes (<paramref name="columns"/>, clé ->
    /// index 0..MaxColumns-1) en respectant leur ordre relatif au sein de
    /// chaque colonne (<paramref name="order"/>, GLOBAL toutes fenêtres/
    /// colonnes confondues — l'ordre RELATIF des lignes d'une même colonne
    /// entre elles donne leur ordre d'affichage de haut en bas) en déplaçant
    /// les Grid déjà existants — pas de gabarit de données à reconstruire,
    /// juste l'ordre/le parent des enfants qui changent, donc tout le reste
    /// (bindings, visibilité, couleurs) reste intact. Toute clé/colonne/
    /// fenêtre manquante ou invalide est corrigée silencieusement par
    /// OverlayConfig.NormalizeRowOrder/NormalizeRowColumns/NormalizeRowWindow
    /// plutôt que de faire disparaître une ligne. Crée ou ferme les
    /// satellites nécessaires AVANT de replacer les lignes, pour qu'une
    /// fenêtre référencée existe toujours au moment d'y ajouter une ligne.
    /// </summary>
    public void ApplyLayout(
        IReadOnlyList<string> order, IReadOnlyDictionary<string, int> columns,
        IReadOnlyDictionary<string, int> rowWindow, IReadOnlyDictionary<int, (int X, int Y)> satelliteWindowPositions)
    {
        var normalizedOrder = OverlayConfig.NormalizeRowOrder(order);
        var normalizedColumns = OverlayConfig.NormalizeRowColumns(columns);
        var normalizedWindow = OverlayConfig.NormalizeRowWindow(rowWindow);

        var neededWindowIds = normalizedWindow.Values.Where(id => id != 0).Distinct().ToList();
        foreach (var existingId in _windows.Keys.Where(id => id != 0).ToList())
        {
            if (!neededWindowIds.Contains(existingId))
                CloseSatellite(existingId);
        }
        foreach (var id in neededWindowIds)
        {
            if (_windows.ContainsKey(id)) continue;
            var (x, y) = satelliteWindowPositions.TryGetValue(id, out var pos) && IsValidScreenPosition(pos.X, pos.Y)
                ? pos
                : ((int)Left + 40, (int)Top + 40);
            CreateSatelliteWindow(id, x, y);
        }

        _rowOrder = normalizedOrder;
        _rowColumn = normalizedColumns;
        _rowWindow = normalizedWindow;
        foreach (var key in _rowOrder)
        {
            var element = _rowGridByKey[key];
            (element.Parent as Panel)?.Children.Remove(element);
            var windowColumns = _windows[_rowWindow[key]].Columns;
            var columnIndex = Math.Clamp(_rowColumn[key], 0, windowColumns.Length - 1);
            windowColumns[columnIndex].Children.Add(element);
        }
        RefreshColumnEditingStrips();
    }

    /// <summary>
    /// Donne à toute colonne VIDE une largeur minimale + un léger lavis cyan
    /// tant que l'overlay est en mode édition, pour qu'elle reste une cible
    /// de dépôt cliquable (glisser une ligne tout à droite pour créer une
    /// nouvelle colonne) — sinon un StackPanel sans enfant occupe une
    /// largeur nulle et ne peut jamais recevoir de première ligne. Hors
    /// édition (ou dès qu'une ligne y est déposée), la colonne retrouve sa
    /// largeur naturelle (0 si toujours vide, invisible) : AUCUNE colonne
    /// n'a de largeur fixe (voir OverlayWindow.xaml), donc l'overlay
    /// rétrécit réellement quand une colonne se vide, et ne s'élargit que
    /// si une ligne y est glissée.
    /// La marge gauche (espacement entre colonnes) est gérée ici plutôt
    /// qu'en XAML pour la même raison : une colonne vide hors édition ne
    /// doit laisser filtrer AUCUN espace, sinon l'overlay ne rétrécirait
    /// que partiellement (une bande vide resterait visible entre les
    /// colonnes restantes). S'applique à TOUTES les fenêtres connues (voir
    /// _windows), pas seulement celle-ci : chaque satellite rétrécit/
    /// s'agrandit exactement de la même façon.
    /// </summary>
    private void RefreshColumnEditingStrips()
    {
        foreach (var entry in _windows.Values)
        {
            var columns = entry.Columns;
            for (var i = 0; i < columns.Length; i++)
            {
                var column = columns[i];
                var empty = column.Children.Count == 0;
                var showsDropStrip = _editMode && empty;
                column.MinWidth = showsDropStrip ? 28 : 0;
                column.Background = showsDropStrip ? new SolidColorBrush(Color.FromArgb(0x14, 0x2D, 0xD4, 0xFF)) : null;
                column.Margin = i == 0 || (empty && !showsDropStrip) ? new Thickness(0) : new Thickness(10, 0, 0, 0);
            }
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
        _lastBgColorHex = bgColorHex;
        _lastBgOpacity = bgOpacityPercent;

        var bgColor = (Color)ColorConverter.ConvertFromString(bgColorHex)!;
        bgColor.A = (byte)Math.Clamp(bgOpacityPercent * 255 / 100, 0, 255);
        PanelBorder.Background = new SolidColorBrush(bgColor);

        var textColor = (Color)ColorConverter.ConvertFromString(textColorHex)!;
        textColor.A = (byte)Math.Clamp(textOpacityPercent * 255 / 100, 0, 255);
        var textBrush = new SolidColorBrush(textColor);
        foreach (var tb in RowTextBlocks())
            tb.Foreground = textBrush;

        // Une fenêtre satellite ne suit pas la couleur de TEXTE ici : les
        // TextBlock de chaque ligne restent des champs de CETTE instance
        // (this) quelle que soit la fenêtre qui les héberge visuellement
        // (voir _rowGridByKey) — la boucle RowTextBlocks() ci-dessus les
        // couvre donc déjà tous. Seul le fond du panneau (PanelBorder)
        // existe séparément dans chaque satellite.
        foreach (var satellite in _satellites.Values)
            satellite.ApplyBackground(bgColorHex, bgOpacityPercent);
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
        _lastScale = scale;
        OverlayScaleTransform.ScaleX = scale;
        OverlayScaleTransform.ScaleY = scale;
        foreach (var satellite in _satellites.Values)
            satellite.ApplyScale(scale);
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
        RefreshColumnEditingStrips();
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
        foreach (var satellite in _satellites.Values)
        {
            satellite.SetClickThrough(!editable);
            satellite.SetEditModeBorder(editable);
        }
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

    /// <summary>
    /// Partagé avec chaque OverlaySatelliteWindow (abonné au même
    /// gestionnaire — voir CreateSatelliteWindow) : DragMove() est appelé
    /// sur <paramref name="sender"/> (la fenêtre qui a REÇU le clic), pas
    /// implicitement sur "this", sinon un clic sur un satellite déplacerait
    /// à tort la fenêtre principale.
    /// </summary>
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
            (sender as Window)?.DragMove();
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

    /// <summary>
    /// Prévisualise en direct le déplacement TANT QUE le curseur reste dans
    /// les limites (écran) d'une fenêtre CONNUE (principale ou satellite) —
    /// voir WindowIdAtScreenPoint. En dehors de toute fenêtre connue, ne
    /// fait RIEN (la ligne reste à sa dernière position valide) : c'est au
    /// relâchement (RowDragHandle_MouseLeftButtonUp) que ce cas déclenche la
    /// création d'une nouvelle fenêtre détachée, pas pendant le glisser —
    /// plus simple et robuste sans prévisualisation live inter-fenêtres.
    /// </summary>
    private void RowDragHandle_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggingKey is null || e.LeftButton != MouseButtonState.Pressed) return;
        var screenPos = _draggingHandle!.PointToScreen(e.GetPosition(_draggingHandle));
        var targetWindowId = WindowIdAtScreenPoint(screenPos);
        if (targetWindowId is null) return;

        var entry = _windows[targetWindowId.Value];
        var positionInColumns = entry.ColumnsPanel.PointFromScreen(screenPos);
        var targetColumn = entry.Columns[ColumnIndexAtX(entry.ColumnsPanel, entry.Columns, positionInColumns.X)];
        var targetIndex = RowIndexAtY(targetColumn, targetColumn.PointFromScreen(screenPos).Y);
        MoveRowToColumnIndex(_draggingKey, targetWindowId.Value, targetColumn, targetIndex);
    }

    private void RowDragHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggingKey is null) return;
        var key = _draggingKey!;
        var screenPos = _draggingHandle!.PointToScreen(e.GetPosition(_draggingHandle));
        _draggingHandle?.ReleaseMouseCapture();
        _draggingHandle = null;
        _draggingKey = null;
        e.Handled = true;

        // Relâché en dehors de toute fenêtre connue : "sortir" la ligne de
        // l'overlay, comme demandé — crée une nouvelle fenêtre détachée à
        // cet endroit plutôt que de laisser la ligne où le dernier
        // MouseMove valide l'avait laissée.
        if (WindowIdAtScreenPoint(screenPos) is null)
            DetachRowToNewWindow(key, screenPos);

        RefreshColumnEditingStrips(); // la colonne quittée peut être redevenue vide, celle rejointe ne l'est plus

        try
        {
            _store.Save(enabled: true, rowOrder: _rowOrder, rowColumns: _rowColumn, rowWindow: _rowWindow);
        }
        catch (Exception ex)
        {
            AppLog.Append(NovaVoxPaths.BaseDirectory, $"[Overlay] Sauvegarde de la disposition des lignes échouée ({ex.Message}).", "diagnostic");
        }
    }

    /// <summary>
    /// Fenêtre (principale ou satellite) dont les limites ÉCRAN contiennent
    /// <paramref name="screenPos"/>, ou null si aucune ne le fait (le
    /// curseur est alors "hors de l'overlay"). Les bornes sont obtenues via
    /// PointToScreen sur CHAQUE fenêtre (comme pour screenPos lui-même)
    /// plutôt qu'en combinant Left/Top (unités indépendantes de la
    /// résolution) avec des coordonnées écran (pixels physiques) : les deux
    /// passent par la même transformation DPI, donc restent comparables même
    /// sur un moniteur à mise à l'échelle non standard.
    /// </summary>
    private int? WindowIdAtScreenPoint(Point screenPos)
    {
        foreach (var (id, entry) in _windows)
        {
            var topLeft = entry.Window.PointToScreen(new Point(0, 0));
            var bottomRight = entry.Window.PointToScreen(new Point(entry.Window.ActualWidth, entry.Window.ActualHeight));
            if (screenPos.X >= topLeft.X && screenPos.X <= bottomRight.X && screenPos.Y >= topLeft.Y && screenPos.Y <= bottomRight.Y)
                return id;
        }
        return null;
    }

    /// <summary>
    /// Trouve la colonne (0..MaxColumns-1) dont le CENTRE horizontal a été
    /// dépassé par <paramref name="x"/> (position dans <paramref name="columnsPanel"/>)
    /// — la dernière colonne active dont le centre est franchi l'emporte,
    /// sans "zone morte" entre deux colonnes ni retour à la colonne 0 une
    /// fois la première dépassée. N'examine que les colonnes actuellement
    /// visibles (largeur non nulle) : une colonne encore masquée (hors
    /// édition ou déjà vide sans bande de dépôt) ne peut pas être une cible.
    /// Statique et paramétrée par <paramref name="columns"/>/<paramref name="columnsPanel"/>
    /// (pas seulement celles de cette fenêtre) pour s'appliquer identiquement
    /// à n'importe quel satellite.
    /// </summary>
    private static int ColumnIndexAtX(Panel columnsPanel, Panel[] columns, double x)
    {
        var best = 0;
        for (var c = 0; c < columns.Length; c++)
        {
            var column = columns[c];
            if (column.ActualWidth <= 0) continue;
            var left = column.TranslatePoint(new Point(0, 0), columnsPanel).X;
            if (x >= left + column.ActualWidth / 2) best = c;
        }
        return best;
    }

    /// <summary>
    /// Trouve la position "insérer avant la ligne i" DANS <paramref name="column"/>
    /// sous la position verticale <paramref name="y"/> (repère de
    /// <paramref name="column"/> lui-même) — bascule au MILIEU de chaque
    /// ligne survolée plutôt qu'à son bord, plus naturel au glisser.
    /// TranslatePoint (position RÉELLEMENT rendue) plutôt qu'une somme
    /// manuelle de ActualHeight/Margin : robuste même pour une ligne à
    /// hauteur variable (ex. RowShipSheet, dont le contenu change de
    /// taille). Retourne column.Children.Count (au-delà de la dernière
    /// ligne) si y dépasse tout le contenu, pour permettre de déposer une
    /// ligne tout en bas — jamais coincée à l'avant-dernière position (voir
    /// MoveRowToColumnIndex pour la compensation de décalage).
    /// </summary>
    private static int RowIndexAtY(Panel column, double y)
    {
        for (var i = 0; i < column.Children.Count; i++)
        {
            if (column.Children[i] is not FrameworkElement fe) continue;
            var top = fe.TranslatePoint(new Point(0, 0), column).Y;
            if (y < top + fe.ActualHeight / 2) return i;
        }
        return column.Children.Count;
    }

    /// <summary>
    /// Déplace la ligne <paramref name="key"/> dans <paramref name="targetColumn"/>
    /// (de la fenêtre <paramref name="targetWindowId"/>) pour qu'elle se
    /// retrouve juste avant l'index <paramref name="targetIndex"/> (voir
    /// RowIndexAtY) DANS L'ORDRE ACTUEL de cette colonne (avant retrait) —
    /// au sein d'UNE MÊME colonne, un retrait à un index inférieur à
    /// targetIndex décale tout ce qui suit d'un cran, d'où la compensation
    /// (targetIndex--) uniquement dans ce cas (déplacement vers le bas dans
    /// la même colonne), sinon l'élément atterrit systématiquement une case
    /// trop loin. Un changement DE colonne (ou de fenêtre) n'a pas besoin de
    /// cette compensation (le retrait a lieu dans un autre panneau que celui
    /// où l'insertion se produit). Ferme le satellite QUITTÉ s'il en résulte
    /// vide (voir CloseSatelliteIfEmpty) — jamais la fenêtre principale.
    /// </summary>
    private void MoveRowToColumnIndex(string key, int targetWindowId, Panel targetColumn, int targetIndex)
    {
        var element = _rowGridByKey[key];
        if (element.Parent is not Panel currentColumn) return;
        var currentIndex = currentColumn.Children.IndexOf(element);
        if (currentIndex < 0) return;

        var sameColumn = ReferenceEquals(currentColumn, targetColumn);
        targetIndex = Math.Clamp(targetIndex, 0, targetColumn.Children.Count);
        if (sameColumn && currentIndex < targetIndex) targetIndex--;
        if (sameColumn && targetIndex == currentIndex) return;

        var previousWindowId = _rowWindow.TryGetValue(key, out var previous) ? previous : 0;

        currentColumn.Children.Remove(element);
        targetColumn.Children.Insert(Math.Clamp(targetIndex, 0, targetColumn.Children.Count), element);

        RecomputeLayoutState();

        // Bascule les bandes de dépôt en direct pendant le glisser, pas
        // seulement au relâchement : sinon une colonne qui vient de se vider
        // (dernière ligne déplacée ailleurs) resterait visible à tort, et
        // une toute nouvelle colonne rejointe ne montrerait sa bande qu'une
        // fois le glisser terminé — moins clair pour viser une 5e position.
        RefreshColumnEditingStrips();

        if (previousWindowId != 0 && previousWindowId != targetWindowId)
            CloseSatelliteIfEmpty(previousWindowId);
    }

    /// <summary>
    /// Reconstruit _rowOrder/_rowColumn/_rowWindow à partir de la position
    /// RÉELLE de chaque Grid dans toutes les fenêtres connues (_windows) —
    /// plus simple et moins sujet aux erreurs qu'un ajustement incrémental
    /// après chaque déplacement, puisque le nombre de fenêtres/colonnes à
    /// considérer change dynamiquement (contrairement à l'ancienne version
    /// mono-fenêtre qui ne parcourait que ColumnPanels).
    /// </summary>
    private void RecomputeLayoutState()
    {
        var order = new List<string>();
        var columnOf = new Dictionary<string, int>();
        var windowOf = new Dictionary<string, int>();
        foreach (var (id, entry) in _windows)
        {
            for (var c = 0; c < entry.Columns.Length; c++)
            {
                foreach (var grid in entry.Columns[c].Children.OfType<Grid>())
                {
                    var key = _rowKeyByGrid[grid];
                    order.Add(key);
                    columnOf[key] = c;
                    windowOf[key] = id;
                }
            }
        }
        _rowOrder = order;
        _rowColumn = columnOf;
        _rowWindow = windowOf;
    }

    /// <summary>
    /// Relâchée hors des limites de toute fenêtre connue : retire la ligne
    /// de sa colonne actuelle et crée une toute nouvelle fenêtre satellite,
    /// juste sous le curseur, ne contenant que cette ligne — exactement le
    /// comportement demandé ("si je déplace une ligne hors de l'overlay,
    /// cela crée un nouvel overlay supplémentaire"). Le nouvel identifiant
    /// est max(id existants)+1 : jamais borné, et réutilise un identifiant
    /// libéré par un satellite refermé entre-temps (CloseSatelliteIfEmpty).
    /// </summary>
    private void DetachRowToNewWindow(string key, Point screenPos)
    {
        var element = _rowGridByKey[key];
        if (element.Parent is not Panel currentColumn) return;
        var previousWindowId = _rowWindow.TryGetValue(key, out var previous) ? previous : 0;
        currentColumn.Children.Remove(element);

        // Simplification assumée : convertit le point ÉCRAN (pixels
        // physiques, comme screenPos) en unités indépendantes de la
        // résolution (Window.Left/Top) via le DPI de LA FENÊTRE PRINCIPALE
        // (this), pas celui du moniteur exact sous le curseur — correct sur
        // un poste à un seul moniteur ou où tous les moniteurs partagent la
        // même mise à l'échelle (le cas courant), légèrement décalé sinon
        // (plusieurs moniteurs à DPI différents) : la nouvelle fenêtre
        // resterait alors proche du point de dépose sans y être exactement,
        // rattrapable en la faisant simplement glisser à nouveau.
        var dpi = VisualTreeHelper.GetDpi(this);
        var newId = _windows.Keys.DefaultIfEmpty(0).Max() + 1;
        CreateSatelliteWindow(newId, screenPos.X / dpi.DpiScaleX, screenPos.Y / dpi.DpiScaleY);
        _windows[newId].Columns[0].Children.Add(element);

        RecomputeLayoutState();
        RefreshColumnEditingStrips();

        if (previousWindowId != 0)
            CloseSatelliteIfEmpty(previousWindowId);
    }

    /// <summary>
    /// Crée et enregistre (_windows/_satellites) une nouvelle fenêtre
    /// détachée à la position donnée (unités indépendantes de la
    /// résolution) — reprend immédiatement l'apparence/échelle/mode édition
    /// COURANTS (voir _lastBgColorHex/_lastBgOpacity/_lastScale/_editMode),
    /// pour qu'elle soit visuellement cohérente avec la fenêtre principale
    /// dès sa création, que ce soit au chargement de la config (plusieurs
    /// satellites à recréer d'un coup) ou en cours de session (un seul
    /// glisser). N'affiche (Show) la nouvelle fenêtre que si l'overlay
    /// principal l'est déjà lui-même (IsVisible) : sinon elle resterait
    /// visible alors que la case "Activer l'overlay" est décochée, jusqu'au
    /// prochain OverlayWindow.Show() qui la reprendra alors normalement (voir
    /// le `new void Show()` ci-dessus).
    /// </summary>
    private OverlaySatelliteWindow CreateSatelliteWindow(int id, double left, double top)
    {
        var satellite = new OverlaySatelliteWindow
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = left,
            Top = top,
        };
        satellite.MouseLeftButtonDown += OnMouseLeftButtonDown;
        satellite.ApplyBackground(_lastBgColorHex, _lastBgOpacity);
        satellite.ApplyScale(_lastScale);
        satellite.SetEditModeBorder(_editMode);
        satellite.SetClickThrough(!_editMode);

        _windows[id] = new OverlayWindowEntry(satellite, satellite.ColumnsPanel, satellite.ColumnPanels);
        _satellites[id] = satellite;

        if (IsVisible) satellite.Show();
        return satellite;
    }

    /// <summary>Ferme le satellite <paramref name="windowId"/> s'il ne contient plus aucune ligne — jamais la fenêtre principale (id 0, toujours conservée même vide).</summary>
    private void CloseSatelliteIfEmpty(int windowId)
    {
        if (windowId == 0) return;
        if (!_windows.TryGetValue(windowId, out var entry)) return;
        if (entry.Columns.Any(c => c.Children.Count > 0)) return;
        CloseSatellite(windowId);
    }

    private void CloseSatellite(int windowId)
    {
        if (!_satellites.TryGetValue(windowId, out var satellite)) return;
        satellite.MouseLeftButtonDown -= OnMouseLeftButtonDown;
        satellite.Close();
        _satellites.Remove(windowId);
        _windows.Remove(windowId);
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _clockTimer.Stop();
        try
        {
            var satelliteWindows = _satellites
                .Where(kv => IsValidScreenPosition(kv.Value.Left, kv.Value.Top))
                .ToDictionary(kv => kv.Key, kv => ((int)kv.Value.Left, (int)kv.Value.Top));
            if (IsValidScreenPosition(Left, Top))
                _store.Save(enabled: true, x: (int)Left, y: (int)Top, rowWindow: _rowWindow, satelliteWindows: satelliteWindows);
            else
                _store.Save(enabled: true, rowWindow: _rowWindow, satelliteWindows: satelliteWindows); // position aberrante : ne pas la persister, la fenêtre se replacera par défaut au prochain lancement
        }
        catch (Exception ex)
        {
            AppLog.Append(NovaVoxPaths.BaseDirectory, $"[Overlay] Sauvegarde de la position/apparence à la fermeture échouée ({ex.Message}).", "diagnostic");
        }

        // Une fenêtre satellite ne doit jamais rester ouverte après la
        // fermeture de la fenêtre principale (sinon l'application ne
        // pourrait plus s'arrêter proprement : ShowInTaskbar="False" les
        // rendrait invisibles mais toujours "ouvertes" pour WPF).
        foreach (var satellite in _satellites.Values.ToList())
            satellite.Close();
    }
}
