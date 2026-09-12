using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Threading;
using NAudio.Wave;
using NovaVox.App.Gemini;
using NovaVox.App.Hotkeys;
using NovaVox.App.Input;
using NovaVox.App.Install;
using NovaVox.App.Overlay;
using NovaVox.App.Speech;
using NovaVox.App.ViewModels;
using NovaVox.App.Voice;
using NovaVox.Core;
using NovaVox.Core.Commands;
using NovaVox.Core.Config;
using NovaVox.Core.GameLog;
using NovaVox.Core.Hotkeys;
using NovaVox.Core.Speech;
using NovaVox.Core.Tts;
using NovaVox.Core.Update;

namespace NovaVox.App;

public partial class MainWindow : Window
{
    private readonly WindowConfigStore _windowConfigStore = new(NovaVoxPaths.BaseDirectory);
    private readonly AppState _state = new(NovaVoxPaths.BaseDirectory);
    private readonly DispatcherTimer _commandsSaveTimer;
    private VoiceOrchestrator? _voiceOrchestrator;
    private bool _restoring = true;

    /// <summary>
    /// true jusqu'à la fin de LoadSettingsIntoControls (voir OnLoaded). Doit
    /// démarrer à true, pas false : les Slider avec un Minimum &gt; 0 sans
    /// Value= explicite dans le XAML (MicGainSlider, TtsVolumeSlider,
    /// PiperLengthScaleSlider) coercent leur Value par défaut (0) au
    /// Minimum dès InitializeComponent(), dans le constructeur — donc AVANT
    /// que LoadSettingsIntoControls ait pu mettre ce garde-fou à true. Avec
    /// un défaut à false, ce ValueChanged de coercition passait le garde et
    /// écrasait aussitôt la vraie valeur chargée depuis le disque par le
    /// Minimum du curseur (ex. le volume de la voix retombait à 0,1 à
    /// chaque lancement, quel que soit le réglage précédent).
    /// </summary>
    private bool _loadingSettings = true;

    private readonly VoskModelInstaller _voskInstaller = new();
    private readonly PiperInstaller _piperInstaller = new();
    private readonly ObservableCollection<VoskModelRowVm> _voskModelRows = new();
    private readonly ObservableCollection<PiperVoiceRowVm> _piperVoiceRows = new();
    private const int MaxLogParagraphs = 300;
    private PiperTtsEngine? _testTts;

    private GeminiClient? _chatGeminiClient;
    private readonly ObservableCollection<GeminiMessageVm> _geminiMessages = new();

    private const int MicGateMax = 4000; // doit correspondre au Maximum de MicGateSlider
    private readonly MicLevelMonitor _micLevelMonitor = new();

    private GameLogWatcher? _gameLogWatcher;
    private readonly ObservableCollection<GameLogPhraseRowVm> _gameLogPhraseRows = new();
    private readonly ObservableCollection<HudOverrideRowVm> _hudOverrideRows = new();
    private readonly ObservableCollection<DestinationAliasRowVm> _destinationAliasRows = new();

    /// <summary>
    /// Mis à true uniquement par le "Quitter" du menu tray (voir
    /// App.QuitApplication) : sans ça, le bouton X de la fenêtre masque
    /// juste la fenêtre au lieu de fermer l'application — port du
    /// comportement tray_state de _wire_main_window_events (app.py).
    /// </summary>
    public bool IsQuitting { get; set; }

    private OverlayWindow? _overlayWindow;

    public MainWindow()
    {
        InitializeComponent();
        MinWidth = WindowConfigStore.MinSize.W;
        MinHeight = WindowConfigStore.MinSize.H;

        // Dès que le HWND existe (avant même l'affichage de la fenêtre, pour
        // éviter un flash de barre de titre blanche) : la sombrit pour
        // suivre le thème de l'appli plutôt que le blanc par défaut de Windows.
        SourceInitialized += (_, _) => DarkTitleBar.Apply(new WindowInteropHelper(this).Handle);

        _commandsSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _commandsSaveTimer.Tick += (_, _) =>
        {
            _commandsSaveTimer.Stop();
            _state.SaveCommands();
        };

        Loaded += OnLoaded;
        Closing += OnClosing;
        SizeChanged += (_, _) => SaveWindowConfig();
        LocationChanged += (_, _) => SaveWindowConfig();

        _micLevelMonitor.LevelChanged += (_, rms) => Dispatcher.BeginInvoke(() => UpdateMicLevelMeter(rms));
        _micLevelMonitor.ErrorOccurred += (_, msg) => Dispatcher.BeginInvoke(() => AppendLog($"[Micro] Mètre de niveau indisponible : {msg}", "warning"));
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

        InitializeCommandsList();
        InitializeProfiles();
        BuildVirtualKeyboard();
        InitializeOverlay();
        LoadSettingsIntoControls();
        InitializeVoiceOrchestrator();
        InitializeVoskCatalog();
        InitializePiperCatalog();
        InitializeGeminiChat();
        InitializeGameLog();
        LoadPanelsBackgroundImage();
        LoadAppLogoImage();
        AppendLog("NovaVox démarré.", "info");
        ThemeManager.Apply(_state.Ai.UiTheme);
        ThemeToggleButton.Content = _state.Ai.UiTheme == "light" ? "☀" : "🌙";
        SyncTitleBarColor();

        var version = VersionUtil.GetAppVersion(Path.Combine(NovaVoxPaths.BaseDirectory, "patch_maj.txt"));
        VersionText.Text = $"NovaVox v{version}";
        FooterVersionButton.Content = $"v{version}";
    }

    private void FooterVersionButton_Click(object sender, RoutedEventArgs e)
    {
        BuildPatchNotesContent();
        PatchNotesOverlay.Visibility = Visibility.Visible;
    }

    private void ClosePatchNotes_Click(object sender, RoutedEventArgs e) => PatchNotesOverlay.Visibility = Visibility.Collapsed;

    /// <summary>Port du rendu de renderPatchNotes (script.js) : un bloc par version (badge "vX.Y.Z" + "ACTUELLE" pour la plus récente), puces et sous-puces.</summary>
    private void BuildPatchNotesContent()
    {
        PatchNotesContent.Children.Clear();
        var patchNotesPath = Path.Combine(NovaVoxPaths.BaseDirectory, "patch_maj.txt");
        var versions = VersionUtil.ParsePatchNotes(VersionUtil.GetPatchNotes(patchNotesPath));

        if (versions.Count == 0)
        {
            PatchNotesContent.Children.Add(new TextBlock
            {
                Text = "Aucune note de mise à jour disponible.",
                Foreground = (Brush)FindResource("MutedBrush"),
                TextWrapping = TextWrapping.Wrap,
            });
            return;
        }

        for (var i = 0; i < versions.Count; i++)
        {
            var version = versions[i];
            var card = new Border
            {
                Background = (Brush)FindResource("PanelAltBrush"),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 12),
            };
            var content = new StackPanel();

            var header = new StackPanel { Orientation = Orientation.Horizontal };
            header.Children.Add(MakePatchNoteBadge($"v{version.Version}", (Brush)FindResource("AccentBrush")));
            if (i == 0)
            {
                var latestBadge = MakePatchNoteBadge("ACTUELLE", (Brush)FindResource("SuccessBrush"));
                latestBadge.Margin = new Thickness(8, 0, 0, 0);
                header.Children.Add(latestBadge);
            }
            content.Children.Add(header);

            foreach (var item in version.Items)
            {
                content.Children.Add(MakePatchNoteLine(item.Text, "•", (Brush)FindResource("AccentBrush"), (Brush)FindResource("TextBrush"), new Thickness(0, 8, 0, 0), 13));
                foreach (var sub in item.Subs)
                    content.Children.Add(MakePatchNoteLine(sub, "◦", (Brush)FindResource("MutedBrush"), (Brush)FindResource("MutedBrush"), new Thickness(24, 4, 0, 0), 12));
            }

            card.Child = content;
            PatchNotesContent.Children.Add(card);
        }
    }

    private static Border MakePatchNoteBadge(string text, Brush foreground) => new()
    {
        Style = (Style)Application.Current.FindResource("CountBadgeStyle"),
        Child = new TextBlock { Text = text, Foreground = foreground, FontWeight = FontWeights.Bold, FontSize = 12 },
    };

    private static StackPanel MakePatchNoteLine(string text, string bullet, Brush bulletBrush, Brush textBrush, Thickness margin, double fontSize)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = margin };
        line.Children.Add(new TextBlock { Text = bullet, Foreground = bulletBrush, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Top, FontSize = fontSize });
        line.Children.Add(new TextBlock { Text = text, Foreground = textBrush, TextWrapping = TextWrapping.Wrap, FontSize = fontSize, MaxWidth = 540 });
        return line;
    }

    /// <summary>Ouvre un lien (Discord, mail...) dans le navigateur/l'application par défaut du système plutôt que dans l'appli elle-même.</summary>
    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    // ------------------------------------------------------------ Commandes

    private void InitializeCommandsList()
    {
        foreach (var row in _state.Commands) HookRow(row);
        _state.Commands.CollectionChanged += (_, args) =>
        {
            if (args.NewItems is not null)
                foreach (VoiceCommandRow row in args.NewItems) HookRow(row);
            ScheduleCommandsSave();
            UpdateCommandCount();
        };
        CommandsList.ItemsSource = _state.Commands;
        UpdateCommandCount();
    }

    private void HookRow(VoiceCommandRow row) => row.PropertyChanged += (_, _) => ScheduleCommandsSave();

    private void UpdateCommandCount() => CommandCountText.Text = _state.Commands.Count(r => !r.IsTitle).ToString();

    private void ScheduleCommandsSave()
    {
        _commandsSaveTimer.Stop();
        _commandsSaveTimer.Start();
    }

    private void AddCommand_Click(object sender, RoutedEventArgs e) =>
        _state.Commands.Add(new VoiceCommandRow { Phrase = "nouvelle commande", Keys = "n" });

    private void AddTitle_Click(object sender, RoutedEventArgs e) =>
        _state.Commands.Add(new VoiceCommandRow { IsTitle = true, Phrase = "Nouveau groupe" });

    private static VoiceCommandRow? RowFromSender(object sender) =>
        (sender as FrameworkElement)?.DataContext as VoiceCommandRow;

    private void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        var row = RowFromSender(sender);
        if (row is null) return;
        var idx = _state.Commands.IndexOf(row);
        if (idx > 0) _state.Commands.Move(idx, idx - 1);
    }

    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        var row = RowFromSender(sender);
        if (row is null) return;
        var idx = _state.Commands.IndexOf(row);
        if (idx >= 0 && idx < _state.Commands.Count - 1) _state.Commands.Move(idx, idx + 1);
    }

    private void DeleteRow_Click(object sender, RoutedEventArgs e)
    {
        var row = RowFromSender(sender);
        if (row is not null) _state.Commands.Remove(row);
    }

    private void SpeakCommand_Click(object sender, RoutedEventArgs e)
    {
        var row = RowFromSender(sender);
        if (row is null || string.IsNullOrWhiteSpace(row.Phrase)) return;
        EnsureTestTts();
        _testTts!.Speak(row.Phrase);
    }

    private void ToggleSynonyms_Click(object sender, RoutedEventArgs e)
    {
        var row = RowFromSender(sender);
        if (row is not null) row.SynonymsExpanded = !row.SynonymsExpanded;
    }

    private void AddSynonym_Click(object sender, RoutedEventArgs e)
    {
        var row = RowFromSender(sender);
        if (row is null) return;
        var value = InputDialog.Show(this, $"Texte mal entendu à associer à « {row.Phrase} » :");
        if (string.IsNullOrWhiteSpace(value)) return;
        var trimmed = value.Trim();
        if (row.Synonyms.Any(s => string.Equals(s, trimmed, StringComparison.OrdinalIgnoreCase))) return;
        row.Synonyms.Add(trimmed);
        row.SynonymsExpanded = true;
    }

    /// <summary>Tag résolu via RelativeSource AncestorType=ListBoxItem (voir DataTemplate imbriqué du XAML) : la commande parente, alors que le DataContext du bouton lui-même est le synonyme (une simple chaîne).</summary>
    private void EditSynonym_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: VoiceCommandRow row, DataContext: string current }) return;
        var idx = row.Synonyms.IndexOf(current);
        if (idx < 0) return;
        var updated = InputDialog.Show(this, $"Modifier le synonyme de « {row.Phrase} » :", current);
        if (string.IsNullOrWhiteSpace(updated)) return;
        row.Synonyms[idx] = updated.Trim();
    }

    private void DeleteSynonym_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: VoiceCommandRow row, DataContext: string current }) return;
        row.Synonyms.Remove(current);
    }

    // --------------------------------------- Réordonnancement par glisser

    private Point _dragStartPoint;
    private VoiceCommandRow? _dragCandidateRow;

    // Pendant un DragDrop.DoDragDrop, WPF ne défile plus la ListBox tout
    // seul (ni molette, ni défilement automatique) : impossible d'atteindre
    // une ligne hors de la zone visible en glissant. On simule donc un
    // défilement automatique quand le curseur reste près du bord haut/bas
    // pendant le survol, via un DispatcherTimer (molette normale hors
    // glisser reste inchangée, gérée nativement par le ScrollViewer interne).
    private const double DragAutoScrollEdge = 40;
    private const double DragAutoScrollStep = 18;
    private ScrollViewer? _dragScrollViewer;
    private DispatcherTimer? _dragScrollTimer;
    private int _dragScrollDirection;

    private void DragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        _dragCandidateRow = (sender as FrameworkElement)?.DataContext as VoiceCommandRow;
    }

    private void CommandsList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragCandidateRow is null) return;
        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var row = _dragCandidateRow;
        _dragCandidateRow = null; // évite de redéclencher DoDragDrop tant que le glisser en cours n'est pas terminé
        _dragScrollViewer ??= FindVisualChild<ScrollViewer>(CommandsList);
        var scrollViewer = _dragScrollViewer;
        using var wheelHook = new DragWheelScrollHook(delta =>
        {
            // Même sens que le défilement standard d'un ScrollViewer : molette
            // vers l'avant (delta > 0) fait remonter le contenu.
            scrollViewer?.ScrollToVerticalOffset(scrollViewer.VerticalOffset - delta / 120.0 * DragAutoScrollStep);
        });
        try
        {
            DragDrop.DoDragDrop(CommandsList, row, DragDropEffects.Move);
        }
        finally
        {
            StopDragAutoScroll();
        }
    }

    private void CommandsList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => _dragCandidateRow = null;

    private void CommandsList_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(VoiceCommandRow)))
        {
            e.Effects = DragDropEffects.None;
            ClearDropIndicators();
            StopDragAutoScroll();
            e.Handled = true;
            return;
        }
        e.Effects = DragDropEffects.Move;
        e.Handled = true;

        UpdateDragAutoScroll(e.GetPosition(CommandsList));

        var targetItem = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        ClearDropIndicators();
        if (targetItem?.DataContext is not VoiceCommandRow targetRow) return;

        // Ligne d'accent en haut/bas de la ligne survolée selon la moitié où
        // se trouve le curseur : seul visuel indiquant où la ligne glissée
        // atterrira, absent jusqu'ici (voir drag-over-top/bottom, script.js).
        if (e.GetPosition(targetItem).Y < targetItem.ActualHeight / 2) targetRow.IsDropTargetTop = true;
        else targetRow.IsDropTargetBottom = true;
    }

    private void CommandsList_DragLeave(object sender, DragEventArgs e)
    {
        ClearDropIndicators();
        StopDragAutoScroll();
    }

    /// <summary>Démarre/arrête/ajuste le défilement automatique selon la proximité du curseur avec le haut ou le bas de la liste visible.</summary>
    private void UpdateDragAutoScroll(Point posInList)
    {
        _dragScrollViewer ??= FindVisualChild<ScrollViewer>(CommandsList);
        var scrollViewer = _dragScrollViewer;
        if (scrollViewer is null) return;

        int direction;
        if (posInList.Y < DragAutoScrollEdge) direction = -1;
        else if (posInList.Y > CommandsList.ActualHeight - DragAutoScrollEdge) direction = 1;
        else direction = 0;

        if (direction == 0)
        {
            StopDragAutoScroll();
            return;
        }
        if (_dragScrollTimer is not null && _dragScrollDirection == direction) return;

        StopDragAutoScroll();
        _dragScrollDirection = direction;
        _dragScrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _dragScrollTimer.Tick += (_, _) =>
            scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + _dragScrollDirection * DragAutoScrollStep);
        _dragScrollTimer.Start();
    }

    private void StopDragAutoScroll()
    {
        _dragScrollTimer?.Stop();
        _dragScrollTimer = null;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            var nested = FindVisualChild<T>(child);
            if (nested is not null) return nested;
        }
        return null;
    }

    private void ClearDropIndicators()
    {
        foreach (var row in _state.Commands)
        {
            row.IsDropTargetTop = false;
            row.IsDropTargetBottom = false;
        }
    }

    private void CommandsList_Drop(object sender, DragEventArgs e)
    {
        ClearDropIndicators();
        StopDragAutoScroll();
        if (e.Data.GetData(typeof(VoiceCommandRow)) is not VoiceCommandRow draggedRow) return;
        var targetItem = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (targetItem?.DataContext is not VoiceCommandRow targetRow || ReferenceEquals(targetRow, draggedRow)) return;

        var fromIndex = _state.Commands.IndexOf(draggedRow);
        var toIndex = _state.Commands.IndexOf(targetRow);
        if (fromIndex < 0 || toIndex < 0) return;

        // insertIndex : position (dans la liste ACTUELLE, avant retrait de
        // l'élément glissé) où il doit atterrir — avant la ligne survolée si
        // le curseur est dans sa moitié haute, après sinon.
        var before = e.GetPosition(targetItem).Y < targetItem.ActualHeight / 2;
        var insertIndex = before ? toIndex : toIndex + 1;
        // ObservableCollection.Move retire d'abord l'élément (ce qui décale
        // tout ce qui suit fromIndex d'un cran), puis l'insère à newIndex
        // DANS la liste déjà réduite : compenser ce décalage si la cible
        // était après le point de départ.
        var newIndex = insertIndex > fromIndex ? insertIndex - 1 : insertIndex;
        if (newIndex == fromIndex) return;
        _state.Commands.Move(fromIndex, newIndex);
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    // ------------------------------------------------- Clavier interactif
    //
    // Port de la modale #keyboardModal (gui/index.html/script.js) :
    // sélection d'une combinaison clavier/souris pour la touche d'une
    // commande, par clic sur le clavier virtuel OU détection physique
    // ("Détecter"). kbState côté Python == (_kbModifiers, _kbMainKey) ici.

    private readonly HashSet<string> _kbModifiers = new();
    private string? _kbMainKey;
    private CancellationTokenSource? _kbCaptureCts;
    private CancellationTokenSource? _kbMouseCaptureCts;
    private Action<string, bool, int, double>? _kbOnConfirm;
    private readonly List<(string Value, bool IsModifier, Button Button)> _kbKeyButtons = new();

    private void BuildVirtualKeyboard()
    {
        BuildKeyboardRows(VirtualKeyboardLayout.MainRows, KbMainPanel);
        BuildKeyboardRows(VirtualKeyboardLayout.NavRows, KbNavPanel);
        BuildKeyboardRows(VirtualKeyboardLayout.NumpadRows, KbNumpadPanel);
        BuildKeyboardRow(VirtualKeyboardLayout.MouseRow, KbMousePanel);
        UpdateKbLayoutButtonHighlight();
        RefreshKeyboardHighlight();
    }

    private void BuildKeyboardRows(VirtualKey[][] rows, StackPanel container)
    {
        foreach (var row in rows)
        {
            var rowPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            BuildKeyboardRow(row, rowPanel);
            container.Children.Add(rowPanel);
        }
    }

    private void BuildKeyboardRow(IEnumerable<VirtualKey> row, StackPanel container)
    {
        foreach (var key in row)
        {
            if (key.Ghost)
            {
                container.Children.Add(new Border { Width = 34, Height = 30, Margin = new Thickness(2) });
                continue;
            }
            var width = key.Wider ? 96.0 : key.Wide ? 62.0 : key.Spacebar ? 150.0 : 34.0;
            var btn = new Button
            {
                Content = key.Label,
                Width = width,
                Height = 30,
                Margin = new Thickness(2),
                Padding = new Thickness(0),
                FontSize = 11,
            };
            var value = key.Value;
            var isModifier = key.IsModifier;
            btn.Click += (_, _) => OnVirtualKeyClick(value, isModifier);
            container.Children.Add(btn);
            _kbKeyButtons.Add((value, isModifier, btn));
        }
    }

    private void OnVirtualKeyClick(string value, bool isModifier)
    {
        if (isModifier)
        {
            if (!_kbModifiers.Remove(value)) _kbModifiers.Add(value);
        }
        else
        {
            _kbMainKey = _kbMainKey == value ? null : value;
        }
        RefreshKeyboardHighlight();
    }

    private string SelectedKbLayoutKey() =>
        _state.Audio.KbLayout is "azerty_fr" or "azerty_be" or "qwerty" ? _state.Audio.KbLayout : "azerty_fr";

    private void RefreshKeyboardHighlight()
    {
        var accent = (Brush)FindResource("AccentBrush");
        var panelAlt = (Brush)FindResource("PanelAltBrush");
        var bg = (Brush)FindResource("BgBrush");
        var text = (Brush)FindResource("TextBrush");

        foreach (var (value, isModifier, btn) in _kbKeyButtons)
        {
            var active = isModifier ? _kbModifiers.Contains(value) : _kbMainKey == value;
            btn.Background = active ? accent : panelAlt;
            btn.Foreground = active ? bg : text;
        }

        var layoutKey = SelectedKbLayoutKey();
        var parts = VirtualKeyboardLayout.ModifierOrder.Where(_kbModifiers.Contains).ToList();
        if (_kbMainKey is not null) parts.Add(_kbMainKey);
        KbSelectedValueText.Text = parts.Count > 0
            ? string.Join(" + ", parts.Select(p => VirtualKeyboardLayout.DisplayLabel(p, layoutKey)))
            : "—";
        KbConfirmButton.IsEnabled = parts.Count > 0;
    }

    private void RefreshKeyboardLabels()
    {
        var layoutKey = SelectedKbLayoutKey();
        foreach (var (value, _, btn) in _kbKeyButtons)
            btn.Content = VirtualKeyboardLayout.DisplayLabel(value, layoutKey);
    }

    private void UpdateKbLayoutButtonHighlight()
    {
        var layoutKey = SelectedKbLayoutKey();
        var accent = (Brush)FindResource("AccentBrush");
        var panelAlt = (Brush)FindResource("PanelAltBrush");
        var bg = (Brush)FindResource("BgBrush");
        var text = (Brush)FindResource("TextBrush");
        foreach (var (btn, tag) in new[] { (KbLayoutFrButton, "azerty_fr"), (KbLayoutBeButton, "azerty_be"), (KbLayoutQwertyButton, "qwerty") })
        {
            var active = tag == layoutKey;
            btn.Background = active ? accent : panelAlt;
            btn.Foreground = active ? bg : text;
        }
    }

    private void KbLayoutButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag }) return;
        _state.Audio.KbLayout = tag;
        _state.SaveAudio();
        SelectComboItemByTag(KbLayoutCombo, tag);
        RefreshKeyboardLabels();
        UpdateKbLayoutButtonHighlight();
        RefreshKeyboardHighlight();
    }

    private void ParseKeysIntoState(string? value)
    {
        _kbModifiers.Clear();
        _kbMainKey = null;
        foreach (var part in (value ?? "").Split('+').Select(p => p.Trim().ToLowerInvariant()).Where(p => p.Length > 0))
        {
            if (VirtualKeyboardLayout.ModifierOrder.Contains(part)) _kbModifiers.Add(part);
            else _kbMainKey = part;
        }
    }

    private static int ParseIntOr(string text, int fallback) => int.TryParse(text, out var v) ? v : fallback;

    private static double ParseDoubleOr(string text, double fallback)
    {
        if (double.TryParse(text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.CurrentCulture, out var v)) return v;
        if (double.TryParse(text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out v)) return v;
        return fallback;
    }

    /// <summary>Ouvre le clavier interactif pré-rempli avec la touche/les options actuelles d'une commande — équivalent de openKeyboard(cb, seedValue) côté Python pour ce cas d'usage précis.</summary>
    private void OpenKeyboardForCommand(VoiceCommandRow row)
    {
        _kbCaptureCts?.Cancel();
        _kbMouseCaptureCts?.Cancel();

        ParseKeysIntoState(row.Keys);

        KbHoldCheckbox.IsChecked = row.Hold;
        var repeatEnabled = row.RepeatCount > 1;
        KbRepeatCheckbox.IsChecked = repeatEnabled;
        KbRepeatFields.Visibility = repeatEnabled ? Visibility.Visible : Visibility.Collapsed;
        KbRepeatCountBox.Text = (repeatEnabled ? row.RepeatCount : 3).ToString();
        KbRepeatDelayBox.Text = row.RepeatDelay.ToString(System.Globalization.CultureInfo.CurrentCulture);

        _kbOnConfirm = (combo, hold, repeatCount, repeatDelay) =>
        {
            row.Keys = combo;
            row.Hold = hold;
            row.RepeatCount = repeatCount;
            row.RepeatDelay = repeatDelay;
            _state.SaveCommands();
        };

        UpdateKbLayoutButtonHighlight();
        RefreshKeyboardHighlight();
        KeyboardOverlay.Visibility = Visibility.Visible;
    }

    private void CommandKeysButton_Click(object sender, RoutedEventArgs e)
    {
        var row = RowFromSender(sender);
        if (row is not null) OpenKeyboardForCommand(row);
    }

    private void CloseKeyboard_Click(object sender, RoutedEventArgs e) => CloseKeyboardOverlay();

    private void KbCancel_Click(object sender, RoutedEventArgs e) => CloseKeyboardOverlay();

    private void CloseKeyboardOverlay()
    {
        _kbCaptureCts?.Cancel();
        _kbMouseCaptureCts?.Cancel();
        _kbOnConfirm = null;
        KeyboardOverlay.Visibility = Visibility.Collapsed;
    }

    private void KbClear_Click(object sender, RoutedEventArgs e)
    {
        _kbModifiers.Clear();
        _kbMainKey = null;
        RefreshKeyboardHighlight();
    }

    private void KbConfirm_Click(object sender, RoutedEventArgs e)
    {
        var parts = VirtualKeyboardLayout.ModifierOrder.Where(_kbModifiers.Contains).ToList();
        if (_kbMainKey is not null) parts.Add(_kbMainKey);
        if (parts.Count == 0) return;
        var combo = string.Join("+", parts);

        var hold = KbHoldCheckbox.IsChecked == true;
        var repeatEnabled = KbRepeatCheckbox.IsChecked == true;
        var repeatCount = repeatEnabled ? Math.Max(2, ParseIntOr(KbRepeatCountBox.Text, 3)) : 1;
        var repeatDelay = Math.Max(0.0, ParseDoubleOr(KbRepeatDelayBox.Text, 0.1));

        var onConfirm = _kbOnConfirm;
        _kbCaptureCts?.Cancel();
        _kbMouseCaptureCts?.Cancel();
        _kbOnConfirm = null;
        KeyboardOverlay.Visibility = Visibility.Collapsed;
        onConfirm?.Invoke(combo, hold, repeatCount, repeatDelay);
    }

    private void KbRepeatCheckbox_Changed(object sender, RoutedEventArgs e) =>
        KbRepeatFields.Visibility = KbRepeatCheckbox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Port de onKeyCapture/startKeyCapture (script.js), via HotkeyCapture.CaptureKeyComboAsync (détection physique clavier).</summary>
    private async void KbCaptureButton_Click(object sender, RoutedEventArgs e)
    {
        if (_kbCaptureCts is not null)
        {
            _kbCaptureCts.Cancel();
            return;
        }
        _kbCaptureCts = new CancellationTokenSource();
        KbCaptureButton.Content = "⏳ Appuie sur une touche... (Échap ou recliquer pour annuler)";
        try
        {
            var result = await HotkeyCapture.CaptureKeyComboAsync(_kbCaptureCts.Token);
            if (result.Ok && result.MainKey is not null)
            {
                _kbModifiers.Clear();
                foreach (var m in result.Modifiers ?? Array.Empty<string>()) _kbModifiers.Add(m);
                _kbMainKey = result.MainKey;
                RefreshKeyboardHighlight();
            }
            else if (result.Reason == CaptureFailureReason.Timeout)
            {
                AppendLog("Aucune touche détectée (15 secondes écoulées). Réessaie.", "warning");
            }
        }
        catch (OperationCanceledException)
        {
            // Annulé via le bouton recliqué : rien à faire.
        }
        finally
        {
            _kbCaptureCts = null;
            KbCaptureButton.Content = "🎙 Détecter (appuyer sur la touche)";
        }
    }

    /// <summary>Port de capture_mouse_button/onCaptureMouseButton (app.py/script.js), via HotkeyCapture.CaptureMouseClickAsync.</summary>
    private async void KbMouseCaptureButton_Click(object sender, RoutedEventArgs e)
    {
        if (_kbMouseCaptureCts is not null)
        {
            _kbMouseCaptureCts.Cancel();
            return;
        }
        _kbMouseCaptureCts = new CancellationTokenSource();
        KbMouseCaptureButton.Content = "⏳ Clique avec la souris... (recliquer ici pour annuler)";
        try
        {
            var result = await HotkeyCapture.CaptureMouseClickAsync(_kbMouseCaptureCts.Token);
            if (result.Ok && result.KeyName is not null)
            {
                _kbMainKey = result.KeyName;
                RefreshKeyboardHighlight();
            }
            else if (result.Reason == CaptureFailureReason.Timeout)
            {
                AppendLog("Aucun clic détecté (15 secondes écoulées). Réessaie.", "warning");
            }
        }
        catch (OperationCanceledException)
        {
            // Annulé via le bouton recliqué : rien à faire.
        }
        finally
        {
            _kbMouseCaptureCts = null;
            KbMouseCaptureButton.Content = "🖱 Détecter (cliquer)";
        }
    }

    // ------------------------------------------------------------- Profils

    private void InitializeProfiles()
    {
        ProfileCombo.ItemsSource = _state.Profiles;
        SelectActiveProfileInCombo();
    }

    private void SelectActiveProfileInCombo()
    {
        ProfileCombo.SelectionChanged -= ProfileCombo_SelectionChanged;
        ProfileCombo.SelectedItem = _state.Profiles.FirstOrDefault(p => p.Id == _state.CommandStore.ActiveProfileId);
        ProfileCombo.SelectionChanged += ProfileCombo_SelectionChanged;
    }

    private void ProfileCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProfileCombo.SelectedItem is not ProfileInfo profile) return;
        if (profile.Id == _state.CommandStore.ActiveProfileId) return;
        _state.SwitchProfile(profile.Id);
        CommandsList.ItemsSource = null;
        InitializeCommandsList();
        // SwitchProfile -> ReloadProfiles vide _state.Profiles et le
        // reremplit avec de nouvelles instances ProfileInfo : le
        // SelectedItem actuel (l'ancienne instance, cliquée par
        // l'utilisateur) ne fait donc plus partie de la collection, et
        // WPF le remet à null — d'où la case qui restait vide après un
        // changement de profil. Repointe explicitement vers la nouvelle
        // instance correspondante.
        SelectActiveProfileInCombo();
    }

    private void NewProfile_Click(object sender, RoutedEventArgs e)
    {
        var name = InputDialog.Show(this, "Nom du nouveau profil :", "Nouveau profil");
        if (string.IsNullOrWhiteSpace(name)) return;
        var id = _state.CommandStore.NewProfileId();
        _state.CommandStore.WriteProfile(id, name.Trim(), new());
        _state.SwitchProfile(id);
        CommandsList.ItemsSource = null;
        InitializeCommandsList();
        SelectActiveProfileInCombo();
    }

    private void RenameProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileCombo.SelectedItem is not ProfileInfo profile) return;
        var name = InputDialog.Show(this, "Nouveau nom du profil :", profile.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        var (_, commands) = _state.CommandStore.ReadProfile(profile.Id);
        _state.CommandStore.WriteProfile(profile.Id, name.Trim(), commands);
        _state.ReloadProfiles();
        SelectActiveProfileInCombo();
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileCombo.SelectedItem is not ProfileInfo profile) return;
        if (_state.Profiles.Count <= 1)
        {
            MessageBox.Show(this, "Impossible de supprimer le dernier profil restant.", "NovaVox");
            return;
        }
        if (MessageBox.Show(this, $"Supprimer le profil « {profile.Name} » ?", "NovaVox", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            return;

        var wasActive = profile.Id == _state.CommandStore.ActiveProfileId;
        File.Delete(_state.CommandStore.ProfilePath(profile.Id));
        _state.ReloadProfiles();

        if (wasActive && _state.Profiles.Count > 0)
            _state.SwitchProfile(_state.Profiles[0].Id);

        CommandsList.ItemsSource = null;
        InitializeCommandsList();
        SelectActiveProfileInCombo();
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
            return;
        }
        _voiceOrchestrator?.Dispose();
        _testTts?.Dispose();
        _micLevelMonitor.Dispose();
        _gameLogWatcher?.Dispose();
        _overlayWindow?.Close();
    }

    // ------------------------------------------------------------ Réglages

    private void LoadSettingsIntoControls()
    {
        _loadingSettings = true;
        try
        {
            var audio = _state.Audio;
            InputDeviceCombo.Items.Clear();
            InputDeviceCombo.Items.Add("Périphérique par défaut");
            for (int i = 0; i < WaveInEvent.DeviceCount; i++)
                InputDeviceCombo.Items.Add(WaveInEvent.GetCapabilities(i).ProductName);
            InputDeviceCombo.SelectedItem = audio.InputDevice ?? "Périphérique par défaut";

            OutputDeviceCombo.Items.Clear();
            OutputDeviceCombo.Items.Add("Périphérique par défaut");
            foreach (var name in AudioDevices.ListOutputDeviceNames())
                OutputDeviceCombo.Items.Add(name);
            OutputDeviceCombo.SelectedItem = audio.OutputDevice ?? "Périphérique par défaut";

            MicGainSlider.Value = audio.MicGain;
            MicGateSlider.Value = audio.MicGate;
            // La case ne montre jamais l'encodage brut "joy:{...}" (illisible) :
            // une touche joystick n'existe que via ListenHotkeyValueText / le
            // bouton "🕹 Bouton joystick" ci-dessous (voir listenHotkeyDisplayLabel, script.js).
            ListenHotkeyBox.Text = JoystickHotkeyCodec.Decode(audio.ListenHotkey) is null ? audio.ListenHotkey ?? "" : "";
            UpdateListenHotkeyDisplay();
            SelectRadioForTag(ListenAlwaysRadio, ListenToggleRadio, ListenPttRadio, audio.ListenMode);
            SelectComboItemByTag(KbLayoutCombo, audio.KbLayout);
            TtsVolumeSlider.Value = audio.TtsVolume;
            AecEnabledCheckbox.IsChecked = audio.AecEnabled;

            var ai = _state.Ai;
            GeminiEnabledCheckbox.IsChecked = ai.GeminiEnabled;
            GeminiApiKeyBox.Text = ai.GeminiApiKey;
            GeminiModelBox.Text = ai.GeminiModel;
            GeminiNameBox.Text = ai.GeminiName;
            SelectComboItemByTag(GeminiResponseLengthCombo, ai.GeminiResponseLength);
            GeminiContextBox.Text = ai.GeminiCustomContext;
            ConfirmCommandsCheckbox.IsChecked = ai.ConfirmCommands;
            RadioEffectCheckbox.IsChecked = ai.RadioEffect;
            PiperLengthScaleSlider.Value = ai.PiperLengthScale;
            PiperNoiseScaleSlider.Value = ai.PiperNoiseScale;

            GameLogEnabledCheckbox.IsChecked = ai.GameLogEnabled;
            GameLogAnnounceCheckbox.IsChecked = ai.GameLogAnnounceEvents;
            PlayerHandleBox.Text = ai.GameLogPlayerHandle;

            var overlay = _state.Overlay;
            OverlayEnabledCheckbox.IsChecked = overlay.Enabled;
            OverlayBgColorBox.Text = overlay.BgColor;
            OverlayBgOpacitySlider.Value = overlay.BgOpacity;
            OverlayTextColorBox.Text = overlay.TextColor;
            OverlayTextOpacitySlider.Value = overlay.TextOpacity;
            SelectComboItemByTag(UiLanguageCombo, ai.UiLanguage);

            AutolaunchCheckbox.IsChecked = NovaVox.App.Autolaunch.StarCitizenAutolaunch.IsEnabled();
        }
        finally
        {
            _loadingSettings = false;
        }
        UpdateMicGateMarker();
    }

    private static void SelectRadioForTag(RadioButton always, RadioButton toggle, RadioButton ptt, string mode)
    {
        switch (mode)
        {
            case "toggle_key": toggle.IsChecked = true; break;
            case "push_to_talk": ptt.IsChecked = true; break;
            default: always.IsChecked = true; break;
        }
    }

    private static void SelectComboItemByTag(ComboBox combo, string? tag)
    {
        foreach (var obj in combo.Items)
        {
            if (obj is ComboBoxItem item && (string)item.Tag == tag)
            {
                combo.SelectedItem = item;
                return;
            }
        }
    }

    private void MicGainSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loadingSettings) return;
        _state.Audio.MicGain = e.NewValue;
        _state.SaveAudio();
    }

    private void MicGateSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateMicGateMarker(); // même pendant le chargement des réglages, pour refléter la valeur restaurée
        if (_loadingSettings) return;
        _state.Audio.MicGate = (int)e.NewValue;
        _state.SaveAudio();
    }

    private void InputDeviceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingSettings) return;
        var selected = InputDeviceCombo.SelectedItem as string;
        _state.Audio.InputDevice = selected == "Périphérique par défaut" ? null : selected;
        _state.SaveAudio();
        // Redémarre le mètre de niveau léger avec le nouveau périphérique
        // (sans effet si l'écoute complète tourne déjà : elle alimente
        // alors le mètre elle-même via VoiceOrchestrator.MicLevelChanged).
        StartMicLevelMonitorIfIdle();
    }

    private void MicLevelMeterTrack_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateMicGateMarker();

    /// <summary>Reflète en direct le niveau capté sur la piste du mètre — appelé depuis MicLevelMonitor (réglages ouverts, écoute complète arrêtée) ou VoiceOrchestrator.MicLevelChanged (écoute complète active).</summary>
    private void UpdateMicLevelMeter(long rms)
    {
        var pct = Math.Clamp(rms / (double)MicGateMax, 0.0, 1.0);
        MicLevelFillBar.Width = MicLevelMeterTrack.ActualWidth * pct;
    }

    /// <summary>Repositionne le repère rouge du seuil de sensibilité sur la piste du mètre et met à jour l'étiquette numérique.</summary>
    private void UpdateMicGateMarker()
    {
        var pct = Math.Clamp(MicGateSlider.Value / MicGateMax, 0.0, 1.0);
        MicGateMarkerLine.Margin = new Thickness(MicLevelMeterTrack.ActualWidth * pct, 0, 0, 0);
        MicGateValueText.Text = MicGateSlider.Value > 0 ? ((int)MicGateSlider.Value).ToString() : "Désactivé";
    }

    /// <summary>
    /// Démarre le mètre de niveau léger (MicLevelMonitor) tant que les
    /// réglages sont ouverts ET que l'écoute complète ne tourne pas déjà
    /// (elle alimente alors le même mètre via VoiceOrchestrator.MicLevelChanged
    /// — les deux ne doivent jamais capter le micro en même temps).
    /// </summary>
    private void StartMicLevelMonitorIfIdle()
    {
        if (SettingsOverlay.Visibility != Visibility.Visible) return;
        if (_voiceOrchestrator?.IsListening == true) return;
        var deviceNumber = AudioDevices.ResolveInputDeviceNumber(_state.Audio.InputDevice);
        _micLevelMonitor.Start(deviceNumber);
    }

    private void OutputDeviceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingSettings) return;
        var selected = OutputDeviceCombo.SelectedItem as string;
        _state.Audio.OutputDevice = selected == "Périphérique par défaut" ? null : selected;
        _state.SaveAudio();
        ApplyLiveVoiceSettings();
    }

    private void ListenMode_Checked(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        if (sender is RadioButton { Tag: string tag })
        {
            _state.Audio.ListenMode = tag;
            _state.SaveAudio();
            _voiceOrchestrator?.UpdateListenHotkeySettings();
        }
    }

    private void ListenHotkeyBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        var value = ListenHotkeyBox.Text.Trim();
        // Vider la case ne doit jamais effacer une touche déjà réglée (ex. un
        // bouton joystick capturé, que cette case ne montre pas) : le seul
        // moyen d'effacer est le bouton "Effacer" (voir onClearListenHotkey, script.js).
        if (value.Length == 0) return;
        _state.Audio.ListenHotkey = value.ToLowerInvariant();
        _state.SaveAudio();
        UpdateListenHotkeyDisplay();
        _voiceOrchestrator?.UpdateListenHotkeySettings();
    }

    private void UpdateListenHotkeyDisplay()
    {
        var hotkey = _state.Audio.ListenHotkey;
        if (string.IsNullOrEmpty(hotkey)) { ListenHotkeyValueText.Text = "Non définie"; return; }
        var joyInfo = JoystickHotkeyCodec.Decode(hotkey);
        ListenHotkeyValueText.Text = joyInfo is not null ? $"🕹 Bouton {joyInfo.Button}" : hotkey;
    }

    private void ClearListenHotkey_Click(object sender, RoutedEventArgs e)
    {
        _state.Audio.ListenHotkey = null;
        _state.SaveAudio();
        ListenHotkeyBox.Text = "";
        UpdateListenHotkeyDisplay();
        _voiceOrchestrator?.UpdateListenHotkeySettings();
        AppendLog("Touche d'activation vocale effacée.", "info");
    }

    private CancellationTokenSource? _joystickCaptureCts;

    /// <summary>Port de capture_joystick_button/onCaptureJoystick (app.py/script.js) : capture le prochain bouton pressé sur une manette (VirPil et autres périphériques DirectInput compris).</summary>
    private async void CaptureJoystickButton_Click(object sender, RoutedEventArgs e)
    {
        if (_joystickCaptureCts is not null)
        {
            _joystickCaptureCts.Cancel();
            return;
        }
        if (_voiceOrchestrator is null) return;

        _joystickCaptureCts = new CancellationTokenSource();
        CaptureJoystickButton.Content = "⏳ Appuie sur le bouton... (annuler)";
        AppendLog("Appuie maintenant sur le bouton du joystick à assigner (15 secondes, ou clique à nouveau pour annuler)...", "info");
        try
        {
            CaptureResult result;
            try
            {
                result = await _voiceOrchestrator.CaptureJoystickButtonAsync(_joystickCaptureCts.Token);
            }
            catch (OperationCanceledException)
            {
                // Task.Delay observe l'annulation immédiatement (avant la
                // prochaine vérification en tête de boucle côté HotkeyCapture) :
                // à traiter comme un résultat "annulé" normal, pas une erreur.
                result = new CaptureResult(false, Reason: CaptureFailureReason.Cancelled);
            }
            if (result.Ok && result.JoystickHotkey is not null)
            {
                _state.Audio.ListenHotkey = result.JoystickHotkey;
                _state.SaveAudio();
                UpdateListenHotkeyDisplay();
                _voiceOrchestrator?.UpdateListenHotkeySettings();
                AppendLog($"Touche d'activation vocale réglée sur « {ListenHotkeyValueText.Text} ».", "success");
            }
            else
            {
                var message = result.Reason switch
                {
                    CaptureFailureReason.NoDevice => "Aucun joystick/manette détecté. Vérifie qu'il est bien branché et reconnu par Windows (le VirPil, par exemple, doit apparaître dans les périphériques de jeu Windows).",
                    CaptureFailureReason.Timeout => "Aucun bouton détecté (15 secondes écoulées). Réessaie.",
                    CaptureFailureReason.Cancelled => "Détection annulée.",
                    _ => "Erreur pendant la détection du bouton.",
                };
                AppendLog(message, result.Reason == CaptureFailureReason.Cancelled ? "info" : "error");
            }
        }
        finally
        {
            _joystickCaptureCts = null;
            CaptureJoystickButton.Content = "🕹 Bouton joystick";
        }
    }

    private void KbLayoutCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingSettings) return;
        if (KbLayoutCombo.SelectedItem is ComboBoxItem { Tag: string tag })
        {
            _state.Audio.KbLayout = tag;
            _state.SaveAudio();
            RefreshKeyboardLabels();
            UpdateKbLayoutButtonHighlight();
            RefreshKeyboardHighlight();
        }
    }

    private void TtsVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loadingSettings) return;
        _state.Audio.TtsVolume = e.NewValue;
        _state.SaveAudio();
        ApplyLiveVoiceSettings();
    }

    private void AecEnabledCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        _state.Audio.AecEnabled = AecEnabledCheckbox.IsChecked ?? false;
        _state.SaveAudio();
    }

    private void GeminiEnabledCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        _state.Ai.GeminiEnabled = GeminiEnabledCheckbox.IsChecked ?? false;
        _state.SaveAi();
    }

    private void GeminiApiKeyBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        _state.Ai.GeminiApiKey = GeminiApiKeyBox.Text.Trim();
        _state.SaveAi();
    }

    private void GeminiModelBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        var value = GeminiModelBox.Text.Trim();
        _state.Ai.GeminiModel = value.Length == 0 ? AiConfig.DefaultGeminiModel : value;
        _state.SaveAi();
    }

    private void GeminiNameBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        var value = GeminiNameBox.Text.Trim();
        _state.Ai.GeminiName = value.Length == 0 ? AiConfig.DefaultGeminiName : value;
        _state.SaveAi();
    }

    private void GeminiResponseLengthCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingSettings) return;
        if (GeminiResponseLengthCombo.SelectedItem is ComboBoxItem { Tag: string tag })
        {
            _state.Ai.GeminiResponseLength = tag;
            _state.SaveAi();
        }
    }

    private void GeminiContextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        _state.Ai.GeminiCustomContext = GeminiContextBox.Text;
        _state.SaveAi();
    }

    private void ConfirmCommandsCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        _state.Ai.ConfirmCommands = ConfirmCommandsCheckbox.IsChecked ?? false;
        _state.SaveAi();
    }

    private void RadioEffectCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        _state.Ai.RadioEffect = RadioEffectCheckbox.IsChecked ?? false;
        _state.SaveAi();
        ApplyLiveVoiceSettings();
    }

    private void PiperLengthScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loadingSettings) return;
        _state.Ai.PiperLengthScale = e.NewValue;
        _state.SaveAi();
        ApplyLiveVoiceSettings();
    }

    private void PiperNoiseScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loadingSettings) return;
        _state.Ai.PiperNoiseScale = e.NewValue;
        _state.SaveAi();
        ApplyLiveVoiceSettings();
    }

    private void GameLogEnabledCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        _state.Ai.GameLogEnabled = GameLogEnabledCheckbox.IsChecked ?? false;
        _state.SaveAi();
        if (_state.Ai.GameLogEnabled) StartGameLogWatcher(); else StopGameLogWatcher();
        RefreshGameLogStatus();
    }

    private void GameLogAnnounceCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        _state.Ai.GameLogAnnounceEvents = GameLogAnnounceCheckbox.IsChecked ?? false;
        _state.SaveAi();
    }

    private void PlayerHandleBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        _state.Ai.GameLogPlayerHandle = PlayerHandleBox.Text.Trim();
        _state.SaveAi();
    }

    private void UiLanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingSettings) return;
        if (UiLanguageCombo.SelectedItem is ComboBoxItem { Tag: string tag })
        {
            _state.Ai.UiLanguage = tag;
            _state.SaveAi();
        }
    }

    private void AutolaunchCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        var enabled = AutolaunchCheckbox.IsChecked ?? false;
        var ok = NovaVox.App.Autolaunch.StarCitizenAutolaunch.SetEnabled(enabled);
        if (!ok)
        {
            AutolaunchCheckbox.IsChecked = !enabled; // échec (ex. session non compilée) : annule visuellement
            MessageBox.Show(
                this,
                "Impossible d'activer le lancement automatique. Cette fonctionnalité n'est disponible que depuis la version installée.",
                "NovaVox");
        }
    }

    private void ResetApplication_Click(object sender, RoutedEventArgs e)
    {
        // Réinitialisation complète (commandes/voix/micro...) : reportée à
        // une passe dédiée (mise à jour/installateur, tâche à part) — pour
        // l'instant, seul un rappel est affiché plutôt que de supprimer
        // silencieusement des fichiers sans filet de sécurité.
        MessageBox.Show(
            this,
            "La réinitialisation complète n'est pas encore disponible dans cette version .NET. " +
            "Supprime manuellement commands.json / ai_config.json / audio_config.json si besoin.",
            "NovaVox");
    }

    // --------------------------------------------------------------- Voix

    private void InitializeVoiceOrchestrator()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        _voiceOrchestrator = new VoiceOrchestrator(_state, hwnd);
        _voiceOrchestrator.Log += (_, e) => Dispatcher.BeginInvoke(() => AppendLog(e.Message, e.Kind));
        _voiceOrchestrator.ListeningChanged += (_, listening) => Dispatcher.BeginInvoke(() =>
        {
            ListenToggleButton.Content = listening ? "■ Couper l'écoute" : "▶ Engager l'écoute";
            StatusLabelText.Text = listening ? "Écoute active" : "Arrêté";
            StatusText.Text = listening ? "Écoute en cours" : "Système en veille";
            StatusDot.Fill = listening ? (System.Windows.Media.Brush)FindResource("SuccessBrush") : (System.Windows.Media.Brush)FindResource("MutedBrush");
            _overlayWindow?.SetListening(listening);
            // L'écoute complète et le mètre de niveau léger ne doivent jamais
            // capter le micro en même temps : l'une alimente le mètre pendant
            // que l'autre est à l'arrêt (voir StartMicLevelMonitorIfIdle).
            if (listening) _micLevelMonitor.Stop();
            else StartMicLevelMonitorIfIdle();
        });
        _voiceOrchestrator.MicLevelChanged += (_, rms) => Dispatcher.BeginInvoke(() => UpdateMicLevelMeter(rms));
        _voiceOrchestrator.MicActiveChanged += (_, active) => Dispatcher.BeginInvoke(() => _overlayWindow?.SetMicActive(active));
        _voiceOrchestrator.CommandExecuted += (_, keys) => Dispatcher.BeginInvoke(() => StatusText.Text = $"Commande : {keys}");
        _voiceOrchestrator.CommandTriggered += (_, e) => Dispatcher.BeginInvoke(() =>
        {
            _overlayWindow?.SetPhrase(e.Phrase);
            _overlayWindow?.SetLastCommand($"{e.Phrase} ({e.KeysLabel})");
            _overlayWindow?.FlashCommand();
        });

        ModelPathText.Text = string.IsNullOrEmpty(_state.Audio.ModelPath)
            ? "Aucun modèle sélectionné"
            : _state.Audio.ModelPath;
    }

    // ------------------------------------------------------- Journal système

    /// <summary>
    /// Port de appendLog(msg, kind) (gui/script.js) : kind = "info" | "success" | "error" | "warning",
    /// plus "diagnostic" (propre au port .NET) : écrit dans Log/*.txt comme
    /// tout le reste, mais jamais affiché dans le journal système — pour
    /// les détails techniques verbeux (génération/lecture Piper, position
    /// de l'overlay...) utiles en cas de bug mais qui n'ont rien à faire
    /// dans le journal visible au quotidien.
    /// RichTextBox plutôt qu'un ItemsControl lié à une collection : seul un
    /// contrôle de texte permet à l'utilisateur de sélectionner/copier le
    /// journal, tout en gardant la couleur par kind (via des Run colorés).
    /// </summary>
    private void AppendLog(string message, string kind = "info")
    {
        AppLog.Append(NovaVoxPaths.BaseDirectory, message, kind);
        if (kind == "diagnostic") return;

        var brush = kind switch
        {
            "success" => (Brush)FindResource("SuccessBrush"),
            "error" => (Brush)FindResource("DangerBrush"),
            "warning" => (Brush)FindResource("AmberBrush"),
            _ => (Brush)FindResource("TextBrush"),
        };

        var paragraph = new Paragraph { Margin = new Thickness(0, 1, 0, 1) };
        paragraph.Inlines.Add(new Run($"{DateTime.Now:HH:mm:ss}  ") { Foreground = (Brush)FindResource("MutedBrush") });
        paragraph.Inlines.Add(new Run(message) { Foreground = brush, FontWeight = kind is "success" or "error" ? FontWeights.SemiBold : FontWeights.Normal });

        LogList.Document.Blocks.Add(paragraph);
        while (LogList.Document.Blocks.Count > MaxLogParagraphs) LogList.Document.Blocks.Remove(LogList.Document.Blocks.FirstBlock);
        LogList.ScrollToEnd();
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e) => LogList.Document.Blocks.Clear();

    private void ListenToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_voiceOrchestrator is null) return;
        if (_voiceOrchestrator.IsListening)
        {
            _voiceOrchestrator.Stop();
        }
        else
        {
            _voiceOrchestrator.Start();
        }
    }

    private void BrowseModel_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Sélectionne le dossier du modèle Vosk" };
        if (dialog.ShowDialog(this) != true) return;

        _state.Audio.ModelPath = dialog.FolderName;
        _state.SaveAudio();
        ModelPathText.Text = dialog.FolderName;
    }

    // ------------------------------------------------------------- Thème

    private void ThemeToggleButton_Click(object sender, RoutedEventArgs e)
    {
        var newTheme = _state.Ai.UiTheme == "light" ? "dark" : "light";
        _state.Ai.UiTheme = newTheme;
        _state.SaveAi();
        ThemeManager.Apply(newTheme);
        ThemeToggleButton.Content = newTheme == "light" ? "☀" : "🌙";
        SyncTitleBarColor();
    }

    /// <summary>Colore la barre de titre native exactement comme le fond de l'en-tête de l'appli, au lieu du gris générique — voir DarkTitleBar.ApplyCaptionColor.</summary>
    private void SyncTitleBarColor()
    {
        if (Application.Current?.Resources["PanelColor"] is not Color color) return;
        DarkTitleBar.ApplyCaptionColor(new WindowInteropHelper(this).Handle, color);
    }

    // ----------------------------------------------------------- Réglages

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        SettingsOverlay.Visibility = Visibility.Visible;
        StartMicLevelMonitorIfIdle();
    }

    private void CloseSettings_Click(object sender, RoutedEventArgs e)
    {
        SettingsOverlay.Visibility = Visibility.Collapsed;
        _micLevelMonitor.Stop();
    }

    // --------------------------------------------------- Assistant Gemini (discussion)

    private void InitializeGeminiChat()
    {
        _chatGeminiClient = new GeminiClient(_state.Ai, _state.AiConfigStore) { GameLogStateProvider = () => _gameLogWatcher?.GetState() };
        _chatGeminiClient.UserMessageAdded += (_, question) => Dispatcher.BeginInvoke(() =>
        {
            _geminiMessages.Add(new GeminiMessageVm { Role = "user", Text = question });
            GeminiChatScrollViewer.ScrollToEnd();
        });
        _chatGeminiClient.ReplyReceived += (_, e) => Dispatcher.BeginInvoke(() =>
        {
            _geminiMessages.Add(new GeminiMessageVm { Role = e.IsError ? "error" : "assistant", Text = e.Reply });
            GeminiChatScrollViewer.ScrollToEnd();
            if (!e.IsError && (GeminiSpeakCheckbox.IsChecked ?? true))
            {
                EnsureTestTts();
                _testTts!.Speak(e.Reply);
            }
        });
        GeminiChatList.ItemsSource = _geminiMessages;
    }

    private void OpenGeminiChat_Click(object sender, RoutedEventArgs e)
    {
        GeminiWakeHintText.Text = string.IsNullOrEmpty(_state.Ai.GeminiApiKey)
            ? "Aucune clé API Gemini configurée (voir ⚙️ Réglages > 🌟 IA Gemini)."
            : $"Dis « {_state.Ai.GeminiName} » pour lui parler à voix haute, ou écris ta question ci-dessous.";
        GeminiOverlay.Visibility = Visibility.Visible;
    }

    private void CloseGeminiChat_Click(object sender, RoutedEventArgs e) => GeminiOverlay.Visibility = Visibility.Collapsed;

    private async void GeminiSend_Click(object sender, RoutedEventArgs e) => await SendGeminiChatMessageAsync();

    private async void GeminiChatInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter) await SendGeminiChatMessageAsync();
    }

    private async Task SendGeminiChatMessageAsync()
    {
        var question = GeminiChatInput.Text.Trim();
        if (question.Length == 0 || _chatGeminiClient is null) return;
        GeminiChatInput.Text = "";
        await _chatGeminiClient.AskTextAsync(question, GeminiSpeakCheckbox.IsChecked ?? true);
    }

    // ------------------------------------------------------------------ Game.log

    private void InitializeGameLog()
    {
        foreach (var key in GameLogPhraseCatalog.OrderedKeys)
        {
            var meta = GameLogPhraseCatalog.Meta[key];
            var hint = meta.Placeholders.Count == 0 ? "" : $" — variables : {string.Join(", ", meta.Placeholders.Select(p => "{" + p + "}"))}";
            _gameLogPhraseRows.Add(new GameLogPhraseRowVm
            {
                Key = key,
                Label = meta.Label + hint,
                PlaceholderHint = hint,
                Text = _state.Ai.GameLogPhrases.GetValueOrDefault(key, GameLogPhraseCatalog.Defaults[key]),
            });
        }
        GameLogPhrasesList.ItemsSource = _gameLogPhraseRows;

        foreach (var (rawText, customText) in _state.Ai.GameLogHudOverrides)
            _hudOverrideRows.Add(new HudOverrideRowVm { RawText = rawText, CustomText = customText });
        GameLogHudOverridesList.ItemsSource = _hudOverrideRows;

        foreach (var (rawKey, customName) in _state.Ai.GameLogDestinationAliases)
            _destinationAliasRows.Add(new DestinationAliasRowVm { RawKey = rawKey, CustomName = customName });
        GameLogDestinationAliasesList.ItemsSource = _destinationAliasRows;

        RefreshGameLogStatus();
        if (_state.Ai.GameLogEnabled) StartGameLogWatcher();
    }

    /// <summary>
    /// Image de fond affichée derrière les listes Commandes/Journal
    /// (raccourcies pour la révéler, voir MainWindow.xaml) — lue directement
    /// depuis un fichier local plutôt qu'embarquée dans l'appli, pour que
    /// l'utilisateur puisse la changer en déposant simplement un fichier
    /// "background.jpg" ou "background.png" à côté de NovaVox.exe, sans
    /// recompiler. Absente : rien ne s'affiche.
    /// </summary>
    private void LoadPanelsBackgroundImage()
    {
        var bitmap = TryLoadLocalImage("background.jpg", "background.jpeg", "background.png");
        if (bitmap is not null) PanelsBackgroundImage.Source = bitmap;
    }

    /// <summary>
    /// Logo affiché dans l'en-tête, à la place du badge vectoriel par
    /// défaut — même principe que LoadPanelsBackgroundImage : un fichier
    /// "logo.png"/".jpg"/".jpeg" déposé à côté de NovaVox.exe, sans
    /// recompiler. Absent : le badge vectoriel reste affiché.
    /// </summary>
    private void LoadAppLogoImage()
    {
        var bitmap = TryLoadLocalImage("logo.png", "logo.jpg", "logo.jpeg");
        if (bitmap is null) return;
        AppLogoImage.Source = bitmap;
        AppLogoImage.Visibility = Visibility.Visible;
        DefaultLogoBadge.Visibility = Visibility.Collapsed;
    }

    private BitmapImage? TryLoadLocalImage(params string[] fileNames)
    {
        var path = fileNames
            .Select(name => Path.Combine(NovaVoxPaths.BaseDirectory, name))
            .FirstOrDefault(File.Exists);
        if (path is null) return null;

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad; // charge tout de suite : ne garde pas le fichier verrouillé
            bitmap.UriSource = new Uri(path);
            bitmap.EndInit();
            return bitmap;
        }
        catch (Exception ex)
        {
            AppendLog($"[Erreur] Image illisible ({Path.GetFileName(path)}) : {ex.Message}", "error");
            return null;
        }
    }

    private void StartGameLogWatcher()
    {
        if (_gameLogWatcher is not null) return;
        _gameLogWatcher = new GameLogWatcher(
            onEvent: evt => Dispatcher.BeginInvoke(() => OnGameLogEvent(evt)),
            playerName: _state.Ai.GameLogPlayerHandle);
        _gameLogWatcher.Start();
        if (_voiceOrchestrator is not null) _voiceOrchestrator.GameLogWatcher = _gameLogWatcher;
    }

    private void StopGameLogWatcher()
    {
        if (_voiceOrchestrator is not null) _voiceOrchestrator.GameLogWatcher = null;
        _gameLogWatcher?.Dispose();
        _gameLogWatcher = null;
    }

    private void RefreshGameLogStatus()
    {
        GameLogStatusText.Text = _state.Ai.GameLogEnabled
            ? "Surveillance du Game.log active."
            : "Surveillance désactivée (voir ⚙️ Réglages > 🛰 Game.log).";
    }

    private void OnGameLogEvent(GameLogEvent evt)
    {
        if (evt.Type is GameLogEventTypes.WatcherStarted or GameLogEventTypes.WatcherError)
        {
            AppendLog(evt.Message ?? evt.Type, evt.Type == GameLogEventTypes.WatcherError ? "error" : "info");
            return;
        }
        if (evt.Type == GameLogEventTypes.NicknameDetected)
        {
            if (string.IsNullOrEmpty(_state.Ai.GameLogPlayerHandle) && !string.IsNullOrEmpty(evt.Nickname))
            {
                _state.Ai.GameLogPlayerHandle = evt.Nickname;
                _state.SaveAi();
                Dispatcher.BeginInvoke(() => PlayerHandleBox.Text = evt.Nickname);
                AppendLog($"Pseudo RSI détecté automatiquement : « {evt.Nickname} ».", "info");
            }
            return;
        }

        var result = GameLogAnnouncer.Build(evt, _state.Ai);
        if (result is null) return;

        AppendLog($"{result.Emoji} {result.Text}".Trim(), "info");
        if (result.RawHudText is not null)
            AppendLog($"   (texte détecté dans le jeu : « {result.RawHudText} »)", "info");
        if (result.UnresolvedDestinationWarning)
            AppendLog($"🛰 Nouvelle destination non reconnue dans le Game.log ({result.DestinationAliasKey}) — ajoutée à 🛰 Game.log > Alias de destinations, prête à être renommée.", "warning");

        if (result.IsNewHudOverride && result.HudOverrideKey is not null)
            _hudOverrideRows.Insert(0, new HudOverrideRowVm { RawText = result.HudOverrideKey, CustomText = _state.Ai.GameLogHudOverrides[result.HudOverrideKey], IsNew = true });
        if (result.IsNewDestinationAlias && result.DestinationAliasKey is not null)
            _destinationAliasRows.Insert(0, new DestinationAliasRowVm { RawKey = result.DestinationAliasKey, CustomName = _state.Ai.GameLogDestinationAliases[result.DestinationAliasKey], IsNew = true });
        if (result.IsNewHudOverride || result.IsNewDestinationAlias) _state.SaveAi();

        if (_state.Ai.GameLogAnnounceEvents && result.Text.Length > 0)
        {
            EnsureTestTts();
            _testTts!.Speak(result.Text);
        }
    }

    private void SaveGameLogPhrase_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not GameLogPhraseRowVm row) return;
        var text = row.Text.Trim();
        if (text.Length == 0)
        {
            _state.Ai.GameLogPhrases.Remove(row.Key);
            row.Text = GameLogPhraseCatalog.Defaults[row.Key];
        }
        else
        {
            _state.Ai.GameLogPhrases[row.Key] = text;
        }
        _state.SaveAi();
        AppendLog($"Phrase « {row.Label} » enregistrée.", "success");
    }

    private void SaveHudOverride_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not HudOverrideRowVm row) return;
        var custom = row.CustomText.Trim();
        _state.Ai.GameLogHudOverrides[row.RawText] = custom.Length == 0 ? row.RawText : custom;
        row.IsNew = false;
        _state.SaveAi();
        AppendLog("Correction de lecture enregistrée.", "success");
    }

    private void DeleteHudOverride_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not HudOverrideRowVm row) return;
        _state.Ai.GameLogHudOverrides.Remove(row.RawText);
        _hudOverrideRows.Remove(row);
        _state.SaveAi();
    }

    private void SaveDestinationAlias_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DestinationAliasRowVm row) return;
        _state.Ai.GameLogDestinationAliases[row.RawKey] = row.CustomName.Trim();
        row.IsNew = false;
        _state.SaveAi();
        AppendLog("Alias de destination enregistré.", "success");
    }

    private void DeleteDestinationAlias_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DestinationAliasRowVm row) return;
        _state.Ai.GameLogDestinationAliases.Remove(row.RawKey);
        _destinationAliasRows.Remove(row);
        _state.SaveAi();
    }

    private void OpenGameLog_Click(object sender, RoutedEventArgs e)
    {
        RefreshGameLogStatus();
        GameLogOverlay.Visibility = Visibility.Visible;
    }

    private void CloseGameLog_Click(object sender, RoutedEventArgs e) => GameLogOverlay.Visibility = Visibility.Collapsed;

    private void EnsureTestTts()
    {
        if (_testTts is not null) return;
        _testTts = new PiperTtsEngine();
        _testTts.ErrorOccurred += (_, msg) => Dispatcher.BeginInvoke(() => AppendLog($"[Erreur voix] {msg}", "error"));
        _testTts.Diagnostic += (_, msg) => Dispatcher.BeginInvoke(() => AppendLog(msg, "diagnostic"));
        ApplyLiveVoiceSettings();
    }

    /// <summary>
    /// À appeler après tout changement des réglages voix Piper (voix,
    /// vitesse "length scale", expressivité "noise scale", volume, effet
    /// radio, périphérique de sortie) pour qu'il s'applique immédiatement à
    /// la lecture des commandes ET des réponses Gemini, même si l'écoute
    /// tourne déjà — sans quoi seul un redémarrage de l'écoute (qui
    /// resynchronise _tts) ou du bouton "Tester" (qui le fait manuellement)
    /// en tenait compte.
    /// </summary>
    private void ApplyLiveVoiceSettings()
    {
        _voiceOrchestrator?.ApplyTtsSettings();
        if (_testTts is null) return;
        _testTts.DefaultPiperVoice = _state.Ai.PiperVoice;
        _testTts.LengthScale = _state.Ai.PiperLengthScale;
        _testTts.NoiseScale = _state.Ai.PiperNoiseScale;
        _testTts.RadioEffectEnabled = _state.Ai.RadioEffect;
        _testTts.Volume = _state.Audio.TtsVolume;
        _testTts.OutputDeviceName = _state.Audio.OutputDevice;
    }

    // ------------------------------------------------------------- Overlay

    private void InitializeOverlay()
    {
        _overlayWindow = new OverlayWindow(_state.OverlayConfigStore);
        _overlayWindow.LoadFromConfig();
        // État initial explicite : au lancement, l'écoute n'a pas encore été
        // démarrée (VoiceOrchestrator ne préviendra qu'au premier Start/Stop),
        // donc sans ceci l'overlay resterait sur son fond "normal" au lieu du
        // rouge "micro coupé" tant que l'utilisateur n'a pas basculé l'écoute.
        _overlayWindow.SetListening(false);
        _overlayWindow.SetMicActive(false);
        AppendLog($"Overlay : activé={_state.Overlay.Enabled}.", "info");
        if (_state.Overlay.Enabled)
        {
            _overlayWindow.Show();
            AppendLog($"Overlay : fenêtre affichée (position {_overlayWindow.Left},{_overlayWindow.Top}). Si le jeu tourne en plein écran EXCLUSIF (pas « fenêtré sans bordure »), aucune fenêtre topmost ne peut s'afficher par-dessus, quel que soit ce que fait NovaVox.", "diagnostic");
        }
    }

    private void OverlayEnabledCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        var enabled = OverlayEnabledCheckbox.IsChecked ?? false;
        _state.Overlay.Enabled = enabled;
        _state.SaveOverlay();
        if (_overlayWindow is null)
        {
            AppendLog("Overlay : case cochée mais la fenêtre d'overlay n'existe pas (jamais initialisée).", "error");
            return;
        }
        if (enabled)
        {
            _overlayWindow.Show();
            AppendLog($"Overlay : affiché (position {_overlayWindow.Left},{_overlayWindow.Top}, visible={_overlayWindow.IsVisible}).", "diagnostic");
        }
        else
        {
            _overlayWindow.Hide();
            AppendLog("Overlay : masqué.", "info");
        }
    }

    private void ToggleOverlayEditButton_Click(object sender, RoutedEventArgs e)
    {
        if (_overlayWindow is null) return;
        _overlayEditMode = !_overlayEditMode;
        _overlayWindow.SetEditMode(_overlayEditMode);
        ToggleOverlayEditButton.Content = _overlayEditMode ? "Verrouiller l'overlay" : "Déplacer l'overlay";
    }

    private bool _overlayEditMode;

    private void OverlayAppearance_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings || _overlayWindow is null) return;
        _state.Overlay.BgColor = OverlayBgColorBox.Text.Trim();
        _state.Overlay.BgOpacity = (int)OverlayBgOpacitySlider.Value;
        _state.Overlay.TextColor = OverlayTextColorBox.Text.Trim();
        _state.Overlay.TextOpacity = (int)OverlayTextOpacitySlider.Value;
        _state.SaveOverlay();
        _overlayWindow.ApplyAppearance(_state.Overlay.BgColor, _state.Overlay.BgOpacity, _state.Overlay.TextColor, _state.Overlay.TextOpacity);
    }

    private void OverlayAppearance_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        OverlayAppearance_Changed(sender, new RoutedEventArgs());

    // ---------------------------------------------- Export/import config

    private void ExportConfig_Click(object sender, RoutedEventArgs e) => ExportConfig(allProfiles: false);

    private void ExportConfigAllProfiles_Click(object sender, RoutedEventArgs e) => ExportConfig(allProfiles: true);

    private void ExportConfig(bool allProfiles)
    {
        var suffix = allProfiles ? "_profils" : "";
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"novavox_config{suffix}_{DateTime.Now:yyyyMMdd_HHmmss}.zip",
            Filter = "Archives ZIP (*.zip)|*.zip",
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            ConfigArchive.Export(NovaVoxPaths.BaseDirectory, dialog.FileName, allProfiles);
            MessageBox.Show(this, $"Configuration exportée vers {dialog.FileName}.", "NovaVox");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Échec de l'export : {ex.Message}", "NovaVox");
        }
    }

    private void ImportConfig_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Archives ZIP (*.zip)|*.zip" };
        if (dialog.ShowDialog(this) != true) return;

        var result = ConfigArchive.Import(NovaVoxPaths.BaseDirectory, dialog.FileName, _state.CommandStore);
        if (!result.Ok)
        {
            MessageBox.Show(this, $"Échec de l'import : {result.Error}", "NovaVox");
            return;
        }

        MessageBox.Show(
            this,
            "Configuration importée. Ferme puis rouvre NovaVox pour appliquer les changements.",
            "NovaVox");
    }

    // --------------------------------------------------------- Mise à jour

    private async void CheckForUpdate_Click(object sender, RoutedEventArgs e)
    {
        UpdateStatusText.Text = "Vérification en cours...";
        var patchNotesPath = Path.Combine(NovaVoxPaths.BaseDirectory, "patch_maj.txt");
        var result = await NovaVox.App.Update.UpdateChecker.CheckForUpdateAsync(patchNotesPath);

        if (!result.Available)
        {
            UpdateStatusText.Text = "Déjà à jour, aucune mise à jour disponible.";
            return;
        }

        UpdateStatusText.Text = $"Nouvelle version disponible : v{result.Version}.";
        if (MessageBox.Show(this, $"Version {result.Version} disponible. Ouvrir la page de téléchargement ?", "NovaVox",
                MessageBoxButton.YesNo) == MessageBoxResult.Yes && result.Url is not null)
        {
            NovaVox.App.Update.UpdateChecker.OpenUpdateUrl(result.Url);
        }
    }

    // ------------------------------------------------ Modèle Vosk (téléchargement)

    private void InitializeVoskCatalog()
    {
        _voskModelRows.Clear();
        foreach (var model in VoskModelCatalog.ModelsForLanguage(_state.Ai.UiLanguage))
            _voskModelRows.Add(new VoskModelRowVm { Model = model });
        VoskModelsList.ItemsSource = _voskModelRows;
    }

    private static VoskModelRowVm? VoskRowFromSender(object sender) => (sender as FrameworkElement)?.DataContext as VoskModelRowVm;

    private async void InstallVoskModel_Click(object sender, RoutedEventArgs e)
    {
        var row = VoskRowFromSender(sender);
        if (row is null || row.IsDownloading) return;

        row.IsDownloading = true;
        row.StatusText = "Préparation...";
        void OnProgress(object? _, (int Percent, string Message) p) => row.StatusText = $"{p.Percent}% — {p.Message}";
        string? failureMessage = null;
        void OnDone(object? _, (bool Success, string Message) d) { if (!d.Success) failureMessage = d.Message; }
        _voskInstaller.Progress += OnProgress;
        _voskInstaller.Done += OnDone;
        try
        {
            var installedPath = await _voskInstaller.InstallAsync(row.Model);
            if (installedPath is not null)
            {
                _state.Audio.ModelPath = installedPath;
                _state.SaveAudio();
                ModelPathText.Text = installedPath;
                row.StatusText = "Installé.";
                AppendLog($"Modèle Vosk « {row.Label} » installé.", "success");
            }
            else
            {
                row.StatusText = "Échec.";
                AppendLog($"[Erreur] Modèle Vosk « {row.Label} » : {failureMessage}", "error");
            }
        }
        finally
        {
            _voskInstaller.Progress -= OnProgress;
            _voskInstaller.Done -= OnDone;
            row.IsDownloading = false;
        }
    }

    // ------------------------------------------------- Moteur & voix Piper

    private void InitializePiperCatalog()
    {
        _piperVoiceRows.Clear();
        foreach (var voice in PiperVoiceCatalog.Voices)
        {
            _piperVoiceRows.Add(new PiperVoiceRowVm
            {
                Voice = voice,
                IsInstalled = _piperInstaller.IsVoiceInstalled(voice.Id),
                IsSelected = voice.Id == _state.Ai.PiperVoice,
            });
        }
        PiperVoicesList.ItemsSource = _piperVoiceRows;
        RefreshPiperEngineStatus();
    }

    private void RefreshPiperEngineStatus()
    {
        var installed = _piperInstaller.IsEngineInstalled;
        InstallPiperEngineButton.Content = installed ? "Réinstaller le moteur Piper" : "Installer le moteur Piper";
        PiperEngineStatusText.Text = installed
            ? "Moteur Piper installé."
            : "Aucune voix ne pourra être testée tant que le moteur n'est pas installé (~60-70 Mo).";
    }

    private static PiperVoiceRowVm? PiperRowFromSender(object sender) => (sender as FrameworkElement)?.DataContext as PiperVoiceRowVm;

    private async void InstallPiperEngine_Click(object sender, RoutedEventArgs e)
    {
        InstallPiperEngineButton.IsEnabled = false;
        void OnProgress(object? _, string message) => PiperEngineStatusText.Text = message;
        void OnDone(object? _, bool success) =>
            AppendLog(success ? "Moteur Piper installé." : "[Erreur Piper] Échec de l'installation du moteur (voir détails ci-dessus).", success ? "success" : "error");
        _piperInstaller.EngineProgress += OnProgress;
        _piperInstaller.EngineInstallDone += OnDone;
        try
        {
            await _piperInstaller.InstallEngineAsync();
        }
        finally
        {
            _piperInstaller.EngineProgress -= OnProgress;
            _piperInstaller.EngineInstallDone -= OnDone;
            InstallPiperEngineButton.IsEnabled = true;
            RefreshPiperEngineStatus();
        }
    }

    private void SelectPiperVoice_Click(object sender, RoutedEventArgs e)
    {
        var row = PiperRowFromSender(sender);
        if (row is null || !row.IsInstalled) return;

        foreach (var other in _piperVoiceRows) other.IsSelected = other.Id == row.Id;
        _state.Ai.PiperVoice = row.Id;
        _state.SaveAi();
        ApplyLiveVoiceSettings();
    }

    private async void DownloadPiperVoice_Click(object sender, RoutedEventArgs e)
    {
        var row = PiperRowFromSender(sender);
        if (row is null || row.IsDownloading || row.IsInstalled) return;

        row.IsDownloading = true;
        row.StatusText = "Préparation...";
        void OnProgress(object? _, (string VoiceId, string Message) p)
        {
            if (p.VoiceId == row.Id) row.StatusText = p.Message;
        }
        _piperInstaller.VoiceProgress += OnProgress;
        try
        {
            await _piperInstaller.DownloadVoiceAsync(row.Id);
            row.IsInstalled = _piperInstaller.IsVoiceInstalled(row.Id);
            row.StatusText = row.IsInstalled ? "Téléchargée." : "Échec du téléchargement.";
            AppendLog(row.IsInstalled ? $"Voix Piper « {row.Label} » téléchargée." : $"[Erreur Piper] Téléchargement de la voix « {row.Label} » échoué.",
                row.IsInstalled ? "success" : "error");
            if (row.IsInstalled && _piperVoiceRows.All(r => !r.IsSelected))
            {
                row.IsSelected = true;
                _state.Ai.PiperVoice = row.Id;
                _state.SaveAi();
                ApplyLiveVoiceSettings();
            }
        }
        finally
        {
            _piperInstaller.VoiceProgress -= OnProgress;
            row.IsDownloading = false;
        }
    }

    private void DeletePiperVoice_Click(object sender, RoutedEventArgs e)
    {
        var row = PiperRowFromSender(sender);
        if (row is null) return;

        _piperInstaller.DeleteVoice(row.Id);
        row.IsInstalled = false;
        row.IsSelected = false;
        row.StatusText = "";
        if (_state.Ai.PiperVoice == row.Id)
        {
            _state.Ai.PiperVoice = null;
            _state.SaveAi();
        }
    }

    private void TestPiperVoice_Click(object sender, RoutedEventArgs e)
    {
        var row = PiperRowFromSender(sender);
        if (row is null || !row.IsInstalled) return;

        EnsureTestTts();
        _testTts!.LengthScale = PiperLengthScaleSlider.Value;
        _testTts.NoiseScale = PiperNoiseScaleSlider.Value;
        _testTts.RadioEffectEnabled = RadioEffectCheckbox.IsChecked ?? false;
        _testTts.Volume = _state.Audio.TtsVolume;
        _testTts.OutputDeviceName = _state.Audio.OutputDevice;
        _testTts.Speak("Ceci est un test de la voix sélectionnée.", row.Id);
    }
}
