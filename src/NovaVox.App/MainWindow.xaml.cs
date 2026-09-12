using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using NAudio.Wave;
using NovaVox.App.Gemini;
using NovaVox.App.Install;
using NovaVox.App.Overlay;
using NovaVox.App.Speech;
using NovaVox.App.ViewModels;
using NovaVox.App.Voice;
using NovaVox.Core;
using NovaVox.Core.Commands;
using NovaVox.Core.Config;
using NovaVox.Core.GameLog;
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
    private bool _loadingSettings;

    private readonly VoskModelInstaller _voskInstaller = new();
    private readonly PiperInstaller _piperInstaller = new();
    private readonly ObservableCollection<VoskModelRowVm> _voskModelRows = new();
    private readonly ObservableCollection<PiperVoiceRowVm> _piperVoiceRows = new();
    private readonly ObservableCollection<LogEntryVm> _logEntries = new();
    private const int MaxLogEntries = 300;
    private PiperTtsEngine? _testTts;

    private GeminiClient? _chatGeminiClient;
    private readonly ObservableCollection<GeminiMessageVm> _geminiMessages = new();

    private GameLogWatcher? _gameLogWatcher;
    private readonly ObservableCollection<GameLogEventVm> _gameLogEvents = new();
    private const int MaxGameLogEvents = 300;

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

        LogList.ItemsSource = _logEntries;
        InitializeCommandsList();
        InitializeProfiles();
        InitializeOverlay();
        LoadSettingsIntoControls();
        InitializeVoiceOrchestrator();
        InitializeVoskCatalog();
        InitializePiperCatalog();
        InitializeGeminiChat();
        InitializeGameLog();
        AppendLog("NovaVox démarré.", "info");
        ThemeManager.Apply(_state.Ai.UiTheme);
        ThemeToggleButton.Content = _state.Ai.UiTheme == "light" ? "☀" : "🌙";

        var version = VersionUtil.GetAppVersion(Path.Combine(NovaVoxPaths.BaseDirectory, "patch_maj.txt"));
        VersionText.Text = $"NovaVox v{version}";
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
            ListenHotkeyBox.Text = audio.ListenHotkey ?? "";
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
    }

    private void OutputDeviceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingSettings) return;
        var selected = OutputDeviceCombo.SelectedItem as string;
        _state.Audio.OutputDevice = selected == "Périphérique par défaut" ? null : selected;
        _state.SaveAudio();
        _voiceOrchestrator?.UpdateOutputDevice(_state.Audio.OutputDevice);
    }

    private void ListenMode_Checked(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        if (sender is RadioButton { Tag: string tag })
        {
            _state.Audio.ListenMode = tag;
            _state.SaveAudio();
        }
    }

    private void ListenHotkeyBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        var value = ListenHotkeyBox.Text.Trim();
        _state.Audio.ListenHotkey = value.Length == 0 ? null : value.ToLowerInvariant();
        _state.SaveAudio();
    }

    private void KbLayoutCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingSettings) return;
        if (KbLayoutCombo.SelectedItem is ComboBoxItem { Tag: string tag })
        {
            _state.Audio.KbLayout = tag;
            _state.SaveAudio();
        }
    }

    private void TtsVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loadingSettings) return;
        _state.Audio.TtsVolume = e.NewValue;
        _state.SaveAudio();
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

    private void RadioEffectCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        _state.Ai.RadioEffect = RadioEffectCheckbox.IsChecked ?? false;
        _state.SaveAi();
    }

    private void PiperLengthScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loadingSettings) return;
        _state.Ai.PiperLengthScale = e.NewValue;
        _state.SaveAi();
    }

    private void PiperNoiseScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loadingSettings) return;
        _state.Ai.PiperNoiseScale = e.NewValue;
        _state.SaveAi();
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
        });
        _voiceOrchestrator.CommandExecuted += (_, keys) => Dispatcher.BeginInvoke(() => StatusText.Text = $"Commande : {keys}");

        ModelPathText.Text = string.IsNullOrEmpty(_state.Audio.ModelPath)
            ? "Aucun modèle sélectionné"
            : _state.Audio.ModelPath;
    }

    // ------------------------------------------------------- Journal système

    /// <summary>Port de appendLog(msg, kind) (gui/script.js) : kind = "info" | "success" | "error" | "warning".</summary>
    private void AppendLog(string message, string kind = "info")
    {
        _logEntries.Add(new LogEntryVm { Time = DateTime.Now.ToString("HH:mm:ss"), Message = message, Kind = kind });
        while (_logEntries.Count > MaxLogEntries) _logEntries.RemoveAt(0);
        LogScrollViewer.ScrollToEnd();

        if (kind == "error") ErrorLog.Append(NovaVoxPaths.BaseDirectory, message);
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e) => _logEntries.Clear();

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
    }

    // ----------------------------------------------------------- Réglages

    private void SettingsButton_Click(object sender, RoutedEventArgs e) => SettingsOverlay.Visibility = Visibility.Visible;

    private void CloseSettings_Click(object sender, RoutedEventArgs e) => SettingsOverlay.Visibility = Visibility.Collapsed;

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
        GameLogEventsList.ItemsSource = _gameLogEvents;
        RefreshGameLogStatus();
        if (_state.Ai.GameLogEnabled) StartGameLogWatcher();
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
        var summary = evt.Type switch
        {
            GameLogEventTypes.ZoneChange => $"Changement de zone : {evt.Zone}",
            GameLogEventTypes.RouteSet => $"Itinéraire défini vers {evt.Destination}",
            GameLogEventTypes.JumpStart => "Saut quantique amorcé",
            GameLogEventTypes.HudNotification => evt.Text ?? "Notification HUD",
            GameLogEventTypes.NicknameDetected => $"Pseudo détecté : {evt.Nickname}",
            GameLogEventTypes.WatcherStarted => evt.Message ?? "Surveillance démarrée",
            GameLogEventTypes.WatcherError => $"[Erreur] {evt.Message}",
            _ => evt.Message ?? evt.Type,
        };

        _gameLogEvents.Add(new GameLogEventVm { Time = DateTime.Now.ToString("HH:mm:ss"), Summary = summary });
        while (_gameLogEvents.Count > MaxGameLogEvents) _gameLogEvents.RemoveAt(0);
        GameLogEventsScrollViewer.ScrollToEnd();

        if (evt.Type == GameLogEventTypes.ZoneChange && _state.Ai.GameLogAnnounceEvents && !string.IsNullOrEmpty(evt.Zone))
        {
            EnsureTestTts();
            _testTts!.Speak($"Zone : {evt.Zone}");
        }
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
        _testTts.Diagnostic += (_, msg) => Dispatcher.BeginInvoke(() => AppendLog(msg, "info"));
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
        if (_state.Overlay.Enabled) _overlayWindow.Show();
    }

    private void OverlayEnabledCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        var enabled = OverlayEnabledCheckbox.IsChecked ?? false;
        _state.Overlay.Enabled = enabled;
        _state.SaveOverlay();
        if (_overlayWindow is null) return;
        if (enabled) _overlayWindow.Show(); else _overlayWindow.Hide();
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
