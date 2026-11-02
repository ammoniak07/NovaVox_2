using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NovaVox.Core.Combat;

namespace NovaVox.App.Combat;

/// <summary>
/// « Mes vaisseaux » : on crée ses vaisseaux (nom libre + modèle) et on
/// choisit l'arme de chaque emplacement (canons, lance-missiles). Tout est
/// enregistré à chaque changement dans my_ships.json (voir MyShipsStore),
/// puis sélectionnable dans le tableau « 🎯 Pénétration ».
/// </summary>
public sealed class MyShipsWindow : Window
{
    private readonly MyShipsStore _store;
    private readonly List<MyShipLoadout> _loadouts;

    private readonly ListBox _list = new();
    private readonly StackPanel _editor = new();
    private readonly TextBox _nameBox = new() { Width = 260 };
    private readonly ComboBox _shipCombo = new() { Width = 260 };
    private readonly StackPanel _groupRows = new();
    private readonly StackPanel _mountRows = new();

    private MyShipLoadout? _current;
    private bool _loading;

    /// <summary>Vaisseau sélectionné à la fermeture (pour le reprendre dans le tableau).</summary>
    public MyShipLoadout? Selected => _current;

    public MyShipsWindow(MyShipsStore store, string? selectName = null)
    {
        _store = store;
        _loadouts = store.Load();

        Title = "NovaVox — Mes vaisseaux";
        Width = 1050;
        Height = 780;
        MinWidth = 800;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        if (Application.Current?.TryFindResource("BaseWindowStyle") is Style style) Style = style;
        if (Application.Current?.MainWindow?.Icon is { } icon) Icon = icon;

        var root = new Grid { Margin = new Thickness(14) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = BuildListPanel();
        Grid.SetColumn(left, 0);
        root.Children.Add(left);

        var right = BuildEditorPanel();
        Grid.SetColumn(right, 2);
        root.Children.Add(right);
        Content = root;

        foreach (var ship in CombatDatabase.Ships.Where(s => s.MountList.Count > 0).OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase))
            _shipCombo.Items.Add(new ComboBoxItem { Content = ship.Name, Tag = ship });

        RefreshList(_loadouts.FirstOrDefault(l => l.Name == selectName) ?? _loadouts.FirstOrDefault());
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };
    }

    // ───────────────────────────── Liste de mes vaisseaux ─────────────────────────────

    private UIElement BuildListPanel()
    {
        var card = Ui.Card();
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new StackPanel();
        title.Children.Add(new TextBlock { Text = "🛠 MES VAISSEAUX", FontSize = 18, FontWeight = FontWeights.Bold });
        title.Children.Add(Ui.Muted("Crée tes vaisseaux et choisis leurs armes et leurs boucliers. Ils apparaissent ensuite (avec une ⭐) dans les onglets Attaque et Défense de la fenêtre Pénétration.", 12, new Thickness(0, 6, 0, 10)));
        grid.Children.Add(title);

        _list.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        _list.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            Ui.HighlightSelection(_list);
            ShowLoadout((_list.SelectedItem as ListBoxItem)?.Tag as MyShipLoadout);
        };
        var listBorder = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Child = _list };
        listBorder.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        Grid.SetRow(listBorder, 1);
        grid.Children.Add(listBorder);

        var buttons = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        buttons.Children.Add(Ui.Button("+ Nouveau", (_, _) => AddLoadout(), new Thickness(0, 0, 6, 6)));
        buttons.Children.Add(Ui.Button("⧉ Dupliquer", (_, _) => DuplicateLoadout(), new Thickness(0, 0, 6, 6)));
        buttons.Children.Add(Ui.Button("🗑 Supprimer", (_, _) => DeleteLoadout(), new Thickness(0, 0, 6, 6)));
        Grid.SetRow(buttons, 2);
        grid.Children.Add(buttons);

        card.Child = grid;
        return card;
    }

    private void RefreshList(MyShipLoadout? select)
    {
        _loading = true;
        _list.Items.Clear();
        ListBoxItem? toSelect = null;
        foreach (var loadout in _loadouts)
        {
            var content = new StackPanel { Margin = new Thickness(8, 5, 8, 5) };
            content.Children.Add(new TextBlock { Text = loadout.Name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            content.Children.Add(Ui.Muted(loadout.Ship, 11, default));
            var item = new ListBoxItem { Content = content, Tag = loadout, Cursor = Cursors.Hand };
            _list.Items.Add(item);
            if (ReferenceEquals(loadout, select)) toSelect = item;
        }
        _list.SelectedItem = toSelect;
        _loading = false;
        Ui.HighlightSelection(_list);
        ShowLoadout(select);
    }

    private void AddLoadout()
    {
        var ship = (_current is not null ? CombatDatabase.FindShip(_current.Ship) : null)
                   ?? CombatDatabase.Ships.Where(s => s.MountList.Count > 0).OrderBy(s => s.Name).First();
        var loadout = new MyShipLoadout { Name = UniqueName("Mon " + ship.Name), Ship = ship.Name };
        _loadouts.Add(loadout);
        Save();
        RefreshList(loadout);
        _nameBox.Focus();
        _nameBox.SelectAll();
    }

    private void DuplicateLoadout()
    {
        if (_current is null) return;
        var copy = new MyShipLoadout
        {
            Name = UniqueName(_current.Name + " (copie)"),
            Ship = _current.Ship,
            Weapons = new Dictionary<string, string>(_current.Weapons),
        };
        _loadouts.Add(copy);
        Save();
        RefreshList(copy);
    }

    private void DeleteLoadout()
    {
        if (_current is null) return;
        var answer = MessageBox.Show(this, $"Supprimer « {_current.Name} » ?", "Mes vaisseaux", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;
        var index = _loadouts.IndexOf(_current);
        _loadouts.Remove(_current);
        Save();
        RefreshList(_loadouts.Count == 0 ? null : _loadouts[Math.Clamp(index, 0, _loadouts.Count - 1)]);
    }

    private string UniqueName(string wanted)
    {
        var name = wanted;
        for (var i = 2; _loadouts.Any(l => string.Equals(l.Name, name, StringComparison.CurrentCultureIgnoreCase)); i++)
            name = $"{wanted} {i}";
        return name;
    }

    // ───────────────────────────── Édition d'un vaisseau ─────────────────────────────

    private UIElement BuildEditorPanel()
    {
        var card = Ui.Card();
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var form = new StackPanel();
        var nameRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        nameRow.Children.Add(Ui.Label("Nom"));
        nameRow.Children.Add(_nameBox);
        nameRow.Children.Add(Ui.Label("Vaisseau", new Thickness(18, 0, 8, 0)));
        nameRow.Children.Add(_shipCombo);
        form.Children.Add(nameRow);

        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        actions.Children.Add(Ui.Button("↺ Tout remettre d'origine", (_, _) => ResetToStock(), new Thickness(0, 0, 8, 0)));
        actions.Children.Add(Ui.Muted("Enregistré automatiquement à chaque changement.", 11, new Thickness(0, 6, 0, 0)));
        form.Children.Add(actions);

        form.Children.Add(Ui.Section("Tout changer d'un coup"));
        form.Children.Add(_groupRows);
        form.Children.Add(Ui.Section("Emplacement par emplacement"));
        _editor.Children.Add(form);
        _editor.Children.Add(_mountRows);

        var scroll = new ScrollViewer { Content = _editor, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1);
        grid.Children.Add(scroll);

        _nameBox.TextChanged += (_, _) =>
        {
            if (_loading || _current is null) return;
            var name = _nameBox.Text.Trim();
            if (name.Length == 0) return;
            _current.Name = name;
            Save();
            if (_list.SelectedItem is ListBoxItem { Content: StackPanel panel } && panel.Children[0] is TextBlock title) title.Text = name;
        };
        _shipCombo.SelectionChanged += (_, _) =>
        {
            if (_loading || _current is null || _shipCombo.SelectedItem is not ComboBoxItem { Tag: CombatShip ship }) return;
            if (ship.Name == _current.Ship) return;
            _current.Ship = ship.Name;
            // Les emplacements ne correspondent plus : on repart des armes d'origine.
            _current.Weapons.Clear();
            Save();
            if (_list.SelectedItem is ListBoxItem { Content: StackPanel panel } && panel.Children.Count > 1 && panel.Children[1] is TextBlock sub) sub.Text = ship.Name;
            BuildMountRows();
        };

        card.Child = grid;
        return card;
    }

    private void ShowLoadout(MyShipLoadout? loadout)
    {
        _current = loadout;
        _editor.Visibility = loadout is null ? Visibility.Collapsed : Visibility.Visible;
        if (loadout is null) return;

        _loading = true;
        _nameBox.Text = loadout.Name;
        _shipCombo.SelectedItem = _shipCombo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag is CombatShip s && s.Name == loadout.Ship);
        _loading = false;
        BuildMountRows();
    }

    private void BuildMountRows()
    {
        _groupRows.Children.Clear();
        _mountRows.Children.Clear();
        if (_current is null) return;
        var ship = CombatDatabase.FindShip(_current.Ship);
        if (ship is null)
        {
            _mountRows.Children.Add(Ui.Muted($"Vaisseau « {_current.Ship} » inconnu dans la base actuelle.", 12, new Thickness(0, 6, 0, 0)));
            return;
        }

        var editable = ship.MountList.Where(m => m.Editable).ToList();
        foreach (var group in editable.GroupBy(m => (m.Kind, m.MinSize, m.MaxSize)).Where(g => g.Count() > 1))
        {
            var first = group.First();
            var count = group.Sum(m => m.Count);
            var label = first.IsShield ? $"Tous les boucliers {SizeText(first)} (×{group.Count()})"
                : first.IsGun ? $"Tous les canons {SizeText(first)} (×{group.Count()})"
                : $"Tous les lance-missiles {SizeText(first)} ({count} projectiles)";
            var current = group.Select(m => _current.WeaponOn(m)).Distinct().ToList();
            var combo = WeaponCombo(first, current.Count == 1 ? current[0] : null, mixed: current.Count > 1);
            combo.SelectionChanged += (_, _) =>
            {
                if (_loading || combo.SelectedItem is not ComboBoxItem { Tag: string name }) return;
                foreach (var mount in group) SetWeapon(mount, name);
                Save();
                BuildMountRows();
            };
            _groupRows.Children.Add(Row(label, null, combo));
        }
        if (_groupRows.Children.Count == 0)
            _groupRows.Children.Add(Ui.Muted("Pas de groupe d'emplacements identiques sur ce vaisseau.", 11, new Thickness(0, 0, 0, 4)));

        foreach (var mount in ship.MountList)
        {
            var combo = WeaponCombo(mount, _current.WeaponOn(mount), mixed: false);
            combo.IsEnabled = mount.Editable;
            if (!mount.Editable) combo.ToolTip = "Arme fixe : le jeu ne permet pas de la changer";
            combo.SelectionChanged += (_, _) =>
            {
                if (_loading || combo.SelectedItem is not ComboBoxItem { Tag: string name }) return;
                SetWeapon(mount, name);
                Save();
                BuildMountRows();
            };
            var detail = mount.IsShield ? $"Bouclier {SizeText(mount)}" : mount.IsGun ? $"Canon {SizeText(mount)}" : $"{mount.Count} × {SizeText(mount)}";
            _mountRows.Children.Add(Row(mount.FrenchLabel, detail, combo));
        }
    }

    private void SetWeapon(CombatMount mount, string name)
    {
        if (_current is null) return;
        if (string.Equals(name, mount.Stock ?? "", StringComparison.OrdinalIgnoreCase)) _current.Weapons.Remove(mount.Id);
        else _current.Weapons[mount.Id] = name;
    }

    private void ResetToStock()
    {
        if (_current is null) return;
        _current.Weapons.Clear();
        Save();
        BuildMountRows();
    }

    private ComboBox WeaponCombo(CombatMount mount, string? selected, bool mixed)
    {
        var combo = new ComboBox { MinWidth = 320 };
        if (mixed) combo.Items.Add(new ComboBoxItem { Content = "— Différentes armes —", Tag = null, IsEnabled = false });
        combo.Items.Add(new ComboBoxItem { Content = "— Vide —", Tag = "" });
        if (mount.IsShield)
        {
            foreach (var shield in CombatDatabase.ShieldsFor(mount))
            {
                var isStock = string.Equals(shield.Name, mount.Stock, StringComparison.OrdinalIgnoreCase) ? "  (d'origine)" : "";
                var grade = string.Join(" ", new[] { shield.Class, shield.Grade }.Where(x => !string.IsNullOrWhiteSpace(x)));
                combo.Items.Add(new ComboBoxItem { Content = $"S{shield.Size} · {shield.Name} — {shield.Hp:N0} PV{(grade.Length > 0 ? " · " + grade : "")}{isStock}", Tag = shield.Name });
            }
        }
        else foreach (var weapon in CombatDatabase.WeaponsFor(mount))
        {
            var stock = string.Equals(weapon.Name, mount.Stock, StringComparison.OrdinalIgnoreCase) ? "  (d'origine)" : "";
            combo.Items.Add(new ComboBoxItem { Content = $"S{weapon.Size} · {weapon.Name} — {weapon.ItemType}{stock}", Tag = weapon.Name });
        }
        _loading = true;
        combo.SelectedItem = mixed
            ? combo.Items[0]
            : combo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag is string t && string.Equals(t, selected ?? "", StringComparison.OrdinalIgnoreCase))
              ?? combo.Items.OfType<ComboBoxItem>().First(i => i.Tag is string t && t.Length == 0);
        _loading = false;
        return combo;
    }

    private static UIElement Row(string label, string? detail, ComboBox combo)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 200 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var name = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = label };
        grid.Children.Add(name);
        if (detail is not null)
        {
            var d = Ui.Muted(detail, 12, default);
            d.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(d, 1);
            grid.Children.Add(d);
        }
        Grid.SetColumn(combo, 2);
        grid.Children.Add(combo);
        return grid;
    }

    private static string SizeText(CombatMount mount) =>
        mount.MinSize > 0 && mount.MinSize != mount.MaxSize ? $"S{mount.MinSize}-S{mount.MaxSize}" : $"S{mount.MaxSize}";

    private void Save()
    {
        try
        {
            _store.Save(_loadouts);
        }
        catch (Exception e)
        {
            MessageBox.Show(this, "Impossible d'enregistrer mes vaisseaux : " + e.Message, "Mes vaisseaux", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}

/// <summary>Petits éléments d'interface communs aux fenêtres « Pénétration » et « Mes vaisseaux ».</summary>
internal static class Ui
{
    public static Border Card()
    {
        var card = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 12, 14, 12) };
        card.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        return card;
    }

    public static TextBlock Muted(string text, double size, Thickness margin)
    {
        var block = new TextBlock { Text = text, FontSize = size, Margin = margin, TextWrapping = TextWrapping.Wrap };
        block.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        return block;
    }

    public static TextBlock Label(string text, Thickness? margin = null) =>
        new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = margin ?? new Thickness(0, 0, 8, 0) };

    public static TextBlock Section(string text)
    {
        var block = new TextBlock { Text = text.ToUpperInvariant(), FontSize = 12, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 10, 0, 4) };
        block.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        return block;
    }

    public static Button Button(string text, RoutedEventHandler click, Thickness margin)
    {
        var button = new Button { Content = text, Margin = margin, Padding = new Thickness(10, 4, 10, 4) };
        button.Click += click;
        return button;
    }

    /// <summary>Le style de l'appli n'affiche pas la sélection d'une liste : on colore l'élément choisi.</summary>
    public static void HighlightSelection(ListBox list)
    {
        foreach (var obj in list.Items)
        {
            if (obj is not ListBoxItem item) continue;
            if (item.IsSelected) item.SetResourceReference(Control.BackgroundProperty, "AccentDimBrush");
            else item.Background = System.Windows.Media.Brushes.Transparent;
        }
    }
}
