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
    // relatif DANS une colonne — voir OverlayConfig.RowOrder), colonne de
    // chaque ligne, + élément en cours de déplacement (null hors glisser).
    private List<string> _rowOrder = OverlayConfig.RowKeys.ToList();
    private Dictionary<string, int> _rowColumn = OverlayConfig.RowKeys.ToDictionary(k => k, _ => 0);
    private string? _draggingKey;
    private FrameworkElement? _draggingHandle;
    private readonly Dictionary<string, Grid> _rowGridByKey;
    private readonly Dictionary<Grid, string> _rowKeyByGrid;
    private Panel[] ColumnPanels => new Panel[] { Column0, Column1, Column2, Column3 };

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
        ApplyLayout(config.RowOrder, config.RowColumns);
        _visibleRows = new Dictionary<string, bool>(config.VisibleRows);
        ApplyRowVisibility(_visibleRows);
        RefreshRowVisualsForEditMode();
    }

    /// <summary>
    /// Répartit les lignes dans leurs colonnes (<paramref name="columns"/>,
    /// clé -> index 0..MaxColumns-1) en respectant leur ordre relatif au
    /// sein de chaque colonne (<paramref name="order"/>, GLOBAL toutes
    /// colonnes confondues — l'ordre RELATIF des lignes d'une même colonne
    /// entre elles donne leur ordre d'affichage de haut en bas) en déplaçant
    /// les Grid déjà existants — pas de gabarit de données à reconstruire,
    /// juste l'ordre/le parent des enfants qui changent, donc tout le reste
    /// (bindings, visibilité, couleurs) reste intact. Toute clé/colonne
    /// manquante ou invalide est corrigée silencieusement par
    /// OverlayConfig.NormalizeRowOrder/NormalizeRowColumns plutôt que de
    /// faire disparaître une ligne.
    /// </summary>
    public void ApplyLayout(IReadOnlyList<string> order, IReadOnlyDictionary<string, int> columns)
    {
        _rowOrder = OverlayConfig.NormalizeRowOrder(order);
        _rowColumn = OverlayConfig.NormalizeRowColumns(columns);
        foreach (var key in _rowOrder)
        {
            var element = _rowGridByKey[key];
            (element.Parent as Panel)?.Children.Remove(element);
            ColumnPanels[_rowColumn[key]].Children.Add(element);
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
    /// largeur naturelle (0 si toujours vide, invisible).
    /// </summary>
    private void RefreshColumnEditingStrips()
    {
        foreach (var column in ColumnPanels)
        {
            var empty = column.Children.Count == 0;
            column.MinWidth = _editMode && empty ? 28 : 0;
            column.Background = _editMode && empty ? new SolidColorBrush(Color.FromArgb(0x14, 0x2D, 0xD4, 0xFF)) : null;
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
        var positionInColumns = e.GetPosition(ColumnsPanel);
        var targetColumn = ColumnPanels[ColumnIndexAtX(positionInColumns.X)];
        var targetIndex = RowIndexAtY(targetColumn, e.GetPosition(targetColumn).Y);
        MoveRowToColumnIndex(_draggingKey, targetColumn, targetIndex);
    }

    private void RowDragHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggingKey is null) return;
        _draggingHandle?.ReleaseMouseCapture();
        _draggingHandle = null;
        _draggingKey = null;
        e.Handled = true;
        RefreshColumnEditingStrips(); // la colonne quittée peut être redevenue vide, celle rejointe ne l'est plus

        try
        {
            _store.Save(enabled: true, rowOrder: _rowOrder, rowColumns: _rowColumn);
        }
        catch (Exception ex)
        {
            AppLog.Append(NovaVoxPaths.BaseDirectory, $"[Overlay] Sauvegarde de la disposition des lignes échouée ({ex.Message}).", "diagnostic");
        }
    }

    /// <summary>
    /// Trouve la colonne (0..MaxColumns-1) dont le CENTRE horizontal a été
    /// dépassé par <paramref name="x"/> (position dans ColumnsPanel) — la
    /// dernière colonne active dont le centre est franchi l'emporte, sans
    /// "zone morte" entre deux colonnes ni retour à la colonne 0 une fois la
    /// première dépassée. N'examine que les colonnes actuellement visibles
    /// (largeur non nulle) : une colonne encore masquée (hors édition ou
    /// déjà vide sans bande de dépôt) ne peut pas être une cible.
    /// </summary>
    private int ColumnIndexAtX(double x)
    {
        var best = 0;
        for (var c = 0; c < ColumnPanels.Length; c++)
        {
            var column = ColumnPanels[c];
            if (column.ActualWidth <= 0) continue;
            var left = column.TranslatePoint(new Point(0, 0), ColumnsPanel).X;
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
    /// pour qu'elle se retrouve juste avant l'index <paramref name="targetIndex"/>
    /// (voir RowIndexAtY) DANS L'ORDRE ACTUEL de cette colonne (avant
    /// retrait) — au sein d'UNE MÊME colonne, un retrait à un index
    /// inférieur à targetIndex décale tout ce qui suit d'un cran, d'où la
    /// compensation (targetIndex--) uniquement dans ce cas (déplacement vers
    /// le bas dans la même colonne), sinon l'élément atterrit
    /// systématiquement une case trop loin. Un changement DE colonne n'a pas
    /// besoin de cette compensation (le retrait a lieu dans un autre panneau
    /// que celui où l'insertion se produit).
    /// </summary>
    private void MoveRowToColumnIndex(string key, Panel targetColumn, int targetIndex)
    {
        var element = _rowGridByKey[key];
        if (element.Parent is not Panel currentColumn) return;
        var currentIndex = currentColumn.Children.IndexOf(element);
        if (currentIndex < 0) return;

        var sameColumn = ReferenceEquals(currentColumn, targetColumn);
        targetIndex = Math.Clamp(targetIndex, 0, targetColumn.Children.Count);
        if (sameColumn && currentIndex < targetIndex) targetIndex--;
        if (sameColumn && targetIndex == currentIndex) return;

        currentColumn.Children.Remove(element);
        targetColumn.Children.Insert(Math.Clamp(targetIndex, 0, targetColumn.Children.Count), element);

        _rowOrder = ColumnPanels.SelectMany(c => c.Children.OfType<Grid>()).Select(g => _rowKeyByGrid[g]).ToList();
        _rowColumn = ColumnPanels
            .SelectMany((c, index) => c.Children.OfType<Grid>().Select(g => (Key: _rowKeyByGrid[g], Index: index)))
            .ToDictionary(t => t.Key, t => t.Index);

        // Bascule les bandes de dépôt en direct pendant le glisser, pas
        // seulement au relâchement : sinon une colonne qui vient de se vider
        // (dernière ligne déplacée ailleurs) resterait visible à tort, et
        // une toute nouvelle colonne rejointe ne montrerait sa bande qu'une
        // fois le glisser terminé — moins clair pour viser une 5e position.
        RefreshColumnEditingStrips();
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
