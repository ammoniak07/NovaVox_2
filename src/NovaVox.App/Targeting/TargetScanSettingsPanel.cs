using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NovaVox.Core.Targeting;

namespace NovaVox.App.Targeting;

/// <summary>
/// Réglages « 🎯 Vaisseau ciblé » (onglet Overlay) : activer la lecture du
/// HUD, définir la zone du nom, tester, annonce vocale, fréquence. Chaque
/// changement est enregistré et appliqué tout de suite (ConfigChanged).
/// </summary>
public sealed class TargetScanSettingsPanel : StackPanel
{
    private readonly TargetScanConfig _config;
    private readonly TargetScanner _scanner;
    private readonly CheckBox _enabled = new() { Content = "Afficher le vaisseau ciblé dans l'overlay", Margin = new Thickness(0, 6, 0, 0) };
    private readonly CheckBox _onlyFocused = new() { Content = "Ne lire l'écran que quand Star Citizen est au premier plan", Margin = new Thickness(0, 6, 0, 0) };
    private readonly CheckBox _speak = new() { Content = "Nova annonce le vaisseau ciblé à voix haute", Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBlock _zoneText = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _testText = new() { Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly Slider _interval = new() { Minimum = 0.5, Maximum = 5, SmallChange = 0.5, LargeChange = 1, TickFrequency = 0.5, IsSnapToTickEnabled = true, Width = 140, Margin = new Thickness(8, 0, 8, 0) };
    private readonly TextBlock _intervalText = new() { VerticalAlignment = VerticalAlignment.Center };
    private bool _loading = true;

    /// <summary>Réglages modifiés (à enregistrer et appliquer).</summary>
    public event Action<TargetScanConfig>? ConfigChanged;

    public TargetScanSettingsPanel(TargetScanConfig config, TargetScanner scanner)
    {
        _config = config;
        _scanner = scanner;
        Margin = new Thickness(0, 16, 0, 0);

        Children.Add(new TextBlock { Text = "🎯 Vaisseau ciblé (lecture du HUD)", FontWeight = FontWeights.Bold });
        Children.Add(Muted(
            "NovaVox lit le nom du vaisseau que tu cibles, là où le jeu l'affiche dans le HUD, et l'affiche dans l'overlay (ligne « 🎯 Cible »). " +
            "Lecture de texte intégrée à Windows, hors ligne. Le jeu doit être en « fenêtré sans bordure ».", new Thickness(0, 2, 0, 0)));
        if (!scanner.OcrAvailable)
        {
            var warn = Muted("⚠ La reconnaissance de texte de Windows n'est pas disponible : ajoute la langue « English (United States) » dans Paramètres Windows > Heure et langue > Langue et région.", new Thickness(0, 6, 0, 0));
            warn.SetResourceReference(TextBlock.ForegroundProperty, "AmberBrush");
            Children.Add(warn);
        }

        Children.Add(_enabled);

        var zoneRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var pick = new Button { Content = "📐 Définir la zone du nom…", Padding = new Thickness(10, 3, 10, 3), ToolTip = "Cible un vaisseau en jeu, reviens ici, puis dessine un cadre autour de son nom dans le HUD" };
        pick.Click += (_, _) => PickZone();
        zoneRow.Children.Add(pick);
        zoneRow.Children.Add(_zoneText);
        Children.Add(zoneRow);

        var testRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var test = new Button { Content = "🔍 Tester (dans 3 s)", Padding = new Thickness(10, 3, 10, 3), ToolTip = "Clique, puis reviens dans le jeu avec un vaisseau ciblé : NovaVox lit la zone 3 secondes plus tard" };
        test.Click += async (_, _) => await TestAsync(test);
        testRow.Children.Add(test);
        Children.Add(testRow);
        Children.Add(_testText);

        Children.Add(_onlyFocused);
        Children.Add(_speak);

        var intervalRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        intervalRow.Children.Add(new TextBlock { Text = "Lire toutes les", VerticalAlignment = VerticalAlignment.Center });
        intervalRow.Children.Add(_interval);
        intervalRow.Children.Add(_intervalText);
        Children.Add(intervalRow);

        _enabled.IsChecked = config.Enabled;
        _onlyFocused.IsChecked = config.OnlyWhenGameFocused;
        _speak.IsChecked = config.Speak;
        _interval.Value = Math.Round(config.IntervalMs / 500.0) * 0.5;
        UpdateTexts();
        _loading = false;

        _enabled.Click += (_, _) =>
        {
            _config.Enabled = _enabled.IsChecked == true;
            if (_config.Enabled && !_config.HasZone) _testText.Text = "Définis d'abord la zone du nom (📐) pour que la lecture démarre.";
            Changed();
        };
        _onlyFocused.Click += (_, _) =>
        {
            _config.OnlyWhenGameFocused = _onlyFocused.IsChecked == true;
            Changed();
        };
        _speak.Click += (_, _) =>
        {
            _config.Speak = _speak.IsChecked == true;
            Changed();
        };
        _interval.ValueChanged += (_, _) =>
        {
            if (_loading) return;
            _config.IntervalMs = (int)Math.Round(_interval.Value * 1000);
            UpdateTexts();
            Changed();
        };
    }

    private void Changed()
    {
        UpdateTexts();
        ConfigChanged?.Invoke(_config);
    }

    private void UpdateTexts()
    {
        _zoneText.Text = _config.HasZone
            ? $"Zone : {_config.Width} × {_config.Height} px, en ({_config.Left}, {_config.Top})"
            : "Zone pas encore définie";
        _intervalText.Text = $"{_interval.Value:0.0} s";
    }

    private void PickZone()
    {
        var current = _config.HasZone ? new System.Drawing.Rectangle(_config.Left, _config.Top, _config.Width, _config.Height) : (System.Drawing.Rectangle?)null;
        var picker = new ScreenRegionPickerWindow(current);
        picker.ShowDialog();
        if (picker.Result is not { } zone) return;
        _config.Left = zone.Left;
        _config.Top = zone.Top;
        _config.Width = zone.Width;
        _config.Height = zone.Height;
        _testText.Text = "Zone enregistrée. Utilise « Tester » avec un vaisseau ciblé pour vérifier la lecture.";
        Changed();
    }

    private async Task TestAsync(Button button)
    {
        if (!_config.HasZone)
        {
            _testText.Text = "Définis d'abord la zone du nom (📐).";
            return;
        }
        button.IsEnabled = false;
        try
        {
            for (var i = 3; i > 0; i--)
            {
                _testText.Text = $"Lecture dans {i} s… retourne dans le jeu avec un vaisseau ciblé.";
                await Task.Delay(1000);
            }
            _testText.Text = "Lecture…";
            var result = await Task.Run(() => _scanner.ScanOnceAsync());
            if (result.Error is not null)
            {
                _testText.Text = "⚠ " + result.Error;
                return;
            }
            var read = result.Lines.Count == 0 ? "(rien lu)" : string.Join(" | ", result.Lines);
            _testText.Text = result.Match is { } match
                ? $"Lu : {read}\n✔ Reconnu : {match.Ship.Name} ({match.Score:P0})"
                : $"Lu : {read}\n✖ Aucun vaisseau reconnu. Vérifie que la zone entoure bien le nom (📐), avec un peu de marge.";
        }
        catch (Exception e)
        {
            _testText.Text = "⚠ Lecture impossible : " + e.Message;
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private static TextBlock Muted(string text, Thickness margin)
    {
        var block = new TextBlock { Text = text, FontSize = 12, Margin = margin, TextWrapping = TextWrapping.Wrap };
        block.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        return block;
    }
}
