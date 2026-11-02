using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NovaVox.Core.Combat;

namespace NovaVox.App.Combat;

/// <summary>
/// Tableau « 🎯 Pénétration » : on choisit une arme de vaisseau, et pour
/// chaque vaisseau du jeu le tableau dit si ses tirs passent le bouclier et
/// le blindage (vert) ou ricochent / sont absorbés (rouge). Données hors
/// ligne : voir <see cref="CombatDatabase"/>, calcul : <see cref="WeaponPenetration"/>.
/// Fenêtre séparée (redimensionnable, peut aller sur un deuxième écran).
/// </summary>
public sealed class WeaponPenetrationWindow : Window
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    private static readonly Brush PassBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x50, 0x35, 0xE0, 0xA1)));
    private static readonly Brush BlockBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0x4D, 0x5E)));
    private static readonly Brush NeutralBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x30, 0x80, 0x80, 0x80)));

    /// <summary>Largeurs des colonnes du tableau des vaisseaux (la première prend le reste).</summary>
    private static readonly double[] ColumnWidths = { 0, 52, 130, 105, 105, 200, 170 };

    private enum SortColumn { Name, Size, Deflection, Armor, Shield, ShieldUp, ShieldDown }

    private readonly ComboBox _myShipCombo = new() { Margin = new Thickness(0, 4, 0, 10) };
    private readonly ComboBox _kindCombo = new() { Width = 140, Margin = new Thickness(0, 0, 8, 0) };
    private readonly ComboBox _sizeCombo = new() { Width = 120 };
    private readonly TextBox _weaponSearch = new() { Margin = new Thickness(0, 8, 0, 6), ToolTip = "Rechercher une arme (nom, type, fabricant)" };
    private readonly ListBox _weaponList = new();
    private readonly StackPanel _weaponInfo = new() { Margin = new Thickness(0, 10, 0, 0) };

    private readonly TextBox _shipSearch = new() { Width = 220, ToolTip = "Rechercher un vaisseau" };
    private readonly ComboBox _showCombo = new() { Width = 260, Margin = new Thickness(10, 0, 0, 0) };
    private readonly TextBlock _summary = new() { FontSize = 13, Margin = new Thickness(0, 8, 0, 8), TextWrapping = TextWrapping.Wrap };
    private readonly Grid _header = new() { Margin = new Thickness(0, 0, 14, 4) };
    private readonly StackPanel _rows = new();

    private CombatWeapon? _weapon;
    private SortColumn _sort = SortColumn.Armor;
    private bool _sortDescending = true;
    private bool _updatingWeapons;

    public WeaponPenetrationWindow()
    {
        Title = "NovaVox — Pénétration des armes";
        Width = 1280;
        Height = 820;
        MinWidth = 900;
        MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (Application.Current?.TryFindResource("BaseWindowStyle") is Style style) Style = style;
        if (Application.Current?.MainWindow?.Icon is { } icon) Icon = icon;

        var root = new Grid { Margin = new Thickness(14) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = BuildWeaponPanel();
        Grid.SetColumn(left, 0);
        root.Children.Add(left);

        var right = BuildShipPanel();
        Grid.SetColumn(right, 2);
        root.Children.Add(right);

        Content = root;

        FillFilters();
        RefreshWeaponList();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };
    }

    // ───────────────────────────── Colonne de gauche : l'arme ─────────────────────────────

    private UIElement BuildWeaponPanel()
    {
        var panel = Card();
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var top = new StackPanel();
        top.Children.Add(new TextBlock { Text = "🎯 ARME UTILISÉE", FontSize = 18, FontWeight = FontWeights.Bold });
        top.Children.Add(Muted("Mon vaisseau (facultatif : ne montre que ses armes d'origine, tourelles, missiles et torpilles compris)", 12, new Thickness(0, 10, 0, 0)));
        top.Children.Add(_myShipCombo);
        var filters = new StackPanel { Orientation = Orientation.Horizontal };
        filters.Children.Add(_kindCombo);
        filters.Children.Add(_sizeCombo);
        top.Children.Add(filters);
        top.Children.Add(_weaponSearch);
        Grid.SetRow(top, 0);
        grid.Children.Add(top);

        _weaponList.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        _weaponList.SelectionChanged += (_, _) =>
        {
            if (_updatingWeapons) return;
            HighlightSelectedWeapon();
            if (_weaponList.SelectedItem is ListBoxItem { Tag: CombatWeapon w }) SelectWeapon(w);
        };
        var listBorder = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Child = _weaponList };
        listBorder.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        Grid.SetRow(listBorder, 1);
        grid.Children.Add(listBorder);

        Grid.SetRow(_weaponInfo, 2);
        grid.Children.Add(_weaponInfo);

        _myShipCombo.SelectionChanged += (_, _) => RefreshWeaponList();
        _kindCombo.SelectionChanged += (_, _) => RefreshWeaponList();
        _sizeCombo.SelectionChanged += (_, _) => RefreshWeaponList();
        _weaponSearch.TextChanged += (_, _) => RefreshWeaponList();

        panel.Child = grid;
        return panel;
    }

    private void FillFilters()
    {
        _myShipCombo.Items.Add(new ComboBoxItem { Content = "— Toutes les armes —", Tag = null });
        foreach (var ship in CombatDatabase.Ships.OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase))
            _myShipCombo.Items.Add(new ComboBoxItem { Content = ship.Name, Tag = ship });
        _myShipCombo.SelectedIndex = 0;

        foreach (var (label, kind) in new[] { ("Toutes les armes", ""), ("Canons", "gun"), ("Missiles", "missile"), ("Torpilles", "torpedo") })
            _kindCombo.Items.Add(new ComboBoxItem { Content = label, Tag = kind });
        _kindCombo.SelectedIndex = 1;

        _sizeCombo.Items.Add(new ComboBoxItem { Content = "Toutes tailles", Tag = 0 });
        foreach (var size in CombatDatabase.Weapons.Select(w => w.Size ?? 0).Where(s => s > 0).Distinct().OrderBy(s => s))
            _sizeCombo.Items.Add(new ComboBoxItem { Content = $"Taille S{size}", Tag = size });
        _sizeCombo.SelectedIndex = 0;

        foreach (var label in new[] { "Tous les vaisseaux", "Seulement ceux que je perce", "Seulement ceux qui résistent" })
            _showCombo.Items.Add(new ComboBoxItem { Content = label });
        _showCombo.SelectedIndex = 0;
    }

    private void RefreshWeaponList()
    {
        if (_kindCombo.SelectedItem is null || _sizeCombo.SelectedItem is null || _myShipCombo.SelectedItem is null) return;
        IEnumerable<CombatWeapon> weapons = CombatDatabase.Weapons;

        if (_myShipCombo.SelectedItem is ComboBoxItem { Tag: CombatShip ship })
            weapons = CombatDatabase.StockWeaponsOf(ship);
        else if (_kindCombo.SelectedItem is ComboBoxItem { Tag: string kind } && kind.Length > 0)
            weapons = weapons.Where(w => w.Kind == kind);

        if (_sizeCombo.SelectedItem is ComboBoxItem { Tag: int size } && size > 0)
            weapons = weapons.Where(w => w.Size == size);

        var query = _weaponSearch.Text.Trim();
        if (query.Length > 0)
            weapons = weapons.Where(w => Contains(w.Name, query) || Contains(w.ItemType, query) || Contains(w.Manufacturer, query));

        _updatingWeapons = true;
        _weaponList.Items.Clear();
        ListBoxItem? toSelect = null;
        foreach (var w in weapons.OrderBy(w => w.Size ?? 0).ThenBy(w => w.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var content = new StackPanel { Margin = new Thickness(8, 4, 8, 4) };
            content.Children.Add(new TextBlock { Text = $"S{w.Size} · {w.Name}", FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            content.Children.Add(Muted($"{w.ItemType} · {DamageTypesText(w)}", 11, default));
            var item = new ListBoxItem { Content = content, Tag = w, Cursor = Cursors.Hand };
            _weaponList.Items.Add(item);
            if (_weapon is not null && w.Name == _weapon.Name) toSelect = item;
        }
        toSelect ??= _weaponList.Items.Count > 0 ? (ListBoxItem)_weaponList.Items[0] : null;
        _weaponList.SelectedItem = toSelect;
        _updatingWeapons = false;

        HighlightSelectedWeapon();
        toSelect?.BringIntoView();
        SelectWeapon(toSelect?.Tag as CombatWeapon);
    }

    /// <summary>Le style de l'appli n'affiche pas la sélection d'une liste : on colore l'arme choisie.</summary>
    private void HighlightSelectedWeapon()
    {
        foreach (ListBoxItem item in _weaponList.Items)
        {
            if (item.IsSelected) item.SetResourceReference(BackgroundProperty, "AccentDimBrush");
            else item.Background = Brushes.Transparent;
        }
    }

    private void SelectWeapon(CombatWeapon? weapon)
    {
        _weapon = weapon;
        FillWeaponInfo();
        RefreshShips();
    }

    private void FillWeaponInfo()
    {
        _weaponInfo.Children.Clear();
        var w = _weapon;
        if (w is null)
        {
            var message = _myShipCombo.SelectedItem is ComboBoxItem { Tag: CombatShip ship } && CombatDatabase.StockWeaponsOf(ship).Count == 0
                ? $"Le {ship.Name} n'a aucune arme d'origine."
                : "Aucune arme ne correspond à ces filtres.";
            _weaponInfo.Children.Add(Muted(message, 12, default));
            return;
        }

        var title = new TextBlock { Text = w.Name, FontSize = 16, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap };
        title.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        _weaponInfo.Children.Add(title);
        _weaponInfo.Children.Add(Muted(string.Join(" · ", new[] { $"Taille S{w.Size}", w.ItemType, w.Manufacturer }.Where(s => !string.IsNullOrWhiteSpace(s))), 12, new Thickness(0, 2, 0, 6)));

        var damage = string.Join(" + ", Kinds(w).Select(k => $"{Num(w.Alpha(k))} {WeaponPenetration.FrenchName(k).ToLowerInvariant()}"));
        InfoLine("Dégâts par projectile", w.Pellets > 1 ? $"{damage} (× {w.Pellets} billes)" : damage);
        if (w.IsGun)
        {
            InfoLine("Dégâts par tir (alpha)", Num(w.AlphaTotal));
            InfoLine("Cadence", $"{Num(w.Rpm, 0)} coups/min");
            InfoLine("DPS", Num(w.Dps, 0));
        }
        InfoLine("Vitesse du projectile", $"{Num(w.Speed, 0)} m/s");
        if (w.Range > 0) InfoLine("Portée", $"{Num(w.Range, 0)} m");
    }

    private void InfoLine(string label, string value)
    {
        var line = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var l = Muted(label, 12, default);
        var v = new TextBlock { Text = value, FontSize = 12, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(v, 1);
        line.Children.Add(l);
        line.Children.Add(v);
        _weaponInfo.Children.Add(line);
    }

    // ───────────────────────────── Colonne de droite : les vaisseaux ─────────────────────────────

    private UIElement BuildShipPanel()
    {
        var panel = Card();
        var grid = new Grid();
        for (var i = 0; i < 4; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var top = new DockPanel();
        top.Children.Add(new TextBlock { Text = "🚀 VAISSEAUX ATTAQUABLES", FontSize = 18, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
        var tools = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        tools.Children.Add(Muted("🔍", 13, new Thickness(0, 0, 6, 0)));
        tools.Children.Add(_shipSearch);
        tools.Children.Add(_showCombo);
        DockPanel.SetDock(tools, Dock.Right);
        top.Children.Insert(0, tools);
        Grid.SetRow(top, 0);
        grid.Children.Add(top);

        Grid.SetRow(_summary, 1);
        grid.Children.Add(_summary);

        BuildHeader();
        Grid.SetRow(_header, 2);
        grid.Children.Add(_header);

        var rowsScroll = new ScrollViewer
        {
            Content = _rows,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        Grid.SetRow(rowsScroll, 4);
        grid.Children.Add(rowsScroll);

        var help = Muted(
            "Vert : l'arme fait des dégâts. Rouge : les tirs ricochent sur le blindage (les dégâts d'un projectile ne dépassent pas son seuil de déflexion) " +
            "ou sont absorbés par le bouclier. Bouclier levé : le bouclier absorbe toute l'énergie et toute la distorsion, et 45 % des dégâts physiques " +
            "quand il est plein (de moins en moins en faiblissant) ; ce qui passe doit encore dépasser le seuil de déflexion. La distorsion ne vise pas " +
            "le blindage mais les composants (seuil 0). Données du jeu " + CombatDatabase.GameVersion + ".",
            11, new Thickness(0, 8, 0, 0));
        help.TextWrapping = TextWrapping.Wrap;
        Grid.SetRow(help, 5);
        grid.Children.Add(help);

        _shipSearch.TextChanged += (_, _) => RefreshShips();
        _showCombo.SelectionChanged += (_, _) => RefreshShips();

        panel.Child = grid;
        return panel;
    }

    private void BuildHeader()
    {
        _header.Children.Clear();
        _header.ColumnDefinitions.Clear();
        AddColumns(_header);
        var titles = new (string Text, SortColumn Column, string Tip)[]
        {
            ("Vaisseau", SortColumn.Name, "Trier par nom"),
            ("Taille", SortColumn.Size, "Taille du vaisseau"),
            ("Déflexion", SortColumn.Deflection, "Seuil de déflexion du blindage pour le type de dégâts de l'arme : un projectile qui ne le dépasse pas ricoche"),
            ("Blindage (PV)", SortColumn.Armor, "Points de vie du blindage"),
            ("Bouclier (PV)", SortColumn.Shield, "Points de vie du bouclier (toutes faces)"),
            ("Bouclier levé", SortColumn.ShieldUp, "Les tirs traversent-ils le bouclier plein, puis le blindage ?"),
            ("Bouclier tombé", SortColumn.ShieldDown, "Les tirs passent-ils le blindage une fois le bouclier tombé ? (×N = marge au-dessus du seuil)"),
        };
        for (var i = 0; i < titles.Length; i++)
        {
            var (text, column, tip) = titles[i];
            var arrow = _sort == column ? (_sortDescending ? " ▼" : " ▲") : "";
            var label = new TextBlock
            {
                Text = text + arrow,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand,
                ToolTip = tip,
                Margin = new Thickness(8, 0, 4, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, _sort == column ? "AccentBrush" : "MutedBrush");
            label.MouseLeftButtonUp += (_, _) =>
            {
                if (_sort == column) _sortDescending = !_sortDescending;
                else
                {
                    _sort = column;
                    _sortDescending = column is not SortColumn.Name;
                }
                BuildHeader();
                RefreshShips();
            };
            Grid.SetColumn(label, i);
            _header.Children.Add(label);
        }
    }

    private static void AddColumns(Grid grid)
    {
        foreach (var width in ColumnWidths)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width <= 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(width) });
    }

    private void RefreshShips()
    {
        _rows.Children.Clear();
        var weapon = _weapon;
        if (weapon is null)
        {
            _summary.Text = "Choisis une arme à gauche.";
            return;
        }

        var results = CombatDatabase.Ships.Select(s => WeaponPenetration.Evaluate(weapon, s)).ToList();
        var pierced = results.Count(r => r.ArmorPenetrated);
        var piercedWithShield = results.Count(r => r.ShieldAndArmorPenetrated);
        _summary.Text = $"{weapon.Name} : passe le blindage de {pierced} vaisseaux sur {results.Count} une fois le bouclier tombé, " +
                        $"et {piercedWithShield} avec le bouclier levé.";

        var query = _shipSearch.Text.Trim();
        IEnumerable<PenetrationResult> shown = results;
        if (query.Length > 0) shown = shown.Where(r => Contains(r.Ship.Name, query) || Contains(r.Ship.FullName, query));
        shown = _showCombo.SelectedIndex switch
        {
            1 => shown.Where(r => r.ArmorPenetrated),
            2 => shown.Where(r => !r.ArmorPenetrated),
            _ => shown,
        };
        shown = Sort(shown);

        var index = 0;
        foreach (var r in shown)
            _rows.Children.Add(BuildRow(r, index++));
        if (index == 0) _rows.Children.Add(Muted("Aucun vaisseau ne correspond.", 12, new Thickness(8)));
    }

    private IEnumerable<PenetrationResult> Sort(IEnumerable<PenetrationResult> results)
    {
        Func<PenetrationResult, double> key = _sort switch
        {
            SortColumn.Size => r => r.Ship.Size ?? 0,
            SortColumn.Deflection => r => r.Checks.Count == 0 ? 0 : r.Checks.Max(c => c.Deflection),
            SortColumn.Shield => r => r.Ship.ShieldHp,
            SortColumn.ShieldUp => r => (r.ShieldAndArmorPenetrated ? 2 : r.LeaksThroughShield ? 1 : 0) + Math.Min(r.ArmorMargin, 1000) / 10000,
            SortColumn.ShieldDown => r => Math.Min(r.ArmorMargin, 1000),
            _ => r => r.Ship.ArmorHp,
        };
        if (_sort == SortColumn.Name)
            return _sortDescending
                ? results.OrderByDescending(r => r.Ship.Name, StringComparer.CurrentCultureIgnoreCase)
                : results.OrderBy(r => r.Ship.Name, StringComparer.CurrentCultureIgnoreCase);
        return _sortDescending
            ? results.OrderByDescending(key).ThenBy(r => r.Ship.Name, StringComparer.CurrentCultureIgnoreCase)
            : results.OrderBy(key).ThenBy(r => r.Ship.Name, StringComparer.CurrentCultureIgnoreCase);
    }

    private UIElement BuildRow(PenetrationResult r, int index)
    {
        var row = new Grid { MinHeight = 26 };
        AddColumns(row);

        var border = new Border { Child = row, Padding = new Thickness(0, 2, 0, 2), ToolTip = r.Ship.FullName + (r.Ship.Manufacturer is { } m ? " — " + m : "") };
        if (index % 2 == 1) border.SetResourceReference(Border.BackgroundProperty, "PanelAltBrush50");

        AddCell(row, 0, r.Ship.Name, null, bold: true);
        AddCell(row, 1, r.Ship.Size is { } size and > 0 ? $"S{size}" : "—", null);
        AddCell(row, 2, DeflectionText(r), null);
        AddCell(row, 3, Num(r.Ship.ArmorHp, 0), null);
        AddCell(row, 4, Num(r.Ship.ShieldHp, 0), null);

        var (upText, upBrush) = ShieldUpVerdict(r);
        AddCell(row, 5, upText, upBrush);
        var (downText, downBrush) = ShieldDownVerdict(r);
        AddCell(row, 6, downText, downBrush);
        return border;
    }

    private static void AddCell(Grid row, int column, string text, Brush? background, bool bold = false)
    {
        var cell = new Border
        {
            Background = background ?? Brushes.Transparent,
            CornerRadius = new CornerRadius(3),
            Margin = new Thickness(2, 0, 2, 0),
            Padding = new Thickness(6, 2, 6, 2),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 12.5,
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            },
        };
        Grid.SetColumn(cell, column);
        row.Children.Add(cell);
    }

    private static string DeflectionText(PenetrationResult r)
    {
        if (r.Checks.Count == 0) return "—";
        if (r.Checks.Count == 1) return Num(r.Checks[0].Deflection, 0);
        return string.Join(" / ", r.Checks.Select(c => $"{ShortKind(c.Kind)} {Num(c.Deflection, 0)}"));
    }

    private static (string, Brush) ShieldUpVerdict(PenetrationResult r)
    {
        if (!r.LeaksThroughShield) return ("✖ Absorbé par le bouclier", BlockBrush);
        var best = r.Checks.Where(c => c.AlphaThroughShield > 0).MaxBy(c => c.Deflection <= 0 ? double.MaxValue : c.AlphaThroughShield / c.Deflection)!;
        var leak = $"{Num(best.ShieldLeak * 100, 0)} % → {Num(best.AlphaThroughShield)}";
        return r.ShieldAndArmorPenetrated
            ? ($"✔ Passe ({leak})", PassBrush)
            : ($"✖ Ricoche ({leak} ≤ {Num(best.Deflection, 0)})", BlockBrush);
    }

    private static (string, Brush) ShieldDownVerdict(PenetrationResult r)
    {
        if (r.Checks.Count == 0) return ("—", NeutralBrush);
        var margin = r.ArmorMargin;
        if (r.ArmorPenetrated)
            return (double.IsPositiveInfinity(margin) ? "✔ Passe (aucun seuil)" : $"✔ Passe (×{Num(margin)})", PassBrush);
        return ($"✖ Ricoche (×{Num(margin)})", BlockBrush);
    }

    // ───────────────────────────── Petits utilitaires ─────────────────────────────

    private static IEnumerable<DamageKind> Kinds(CombatWeapon w) =>
        new[] { DamageKind.Physical, DamageKind.Energy, DamageKind.Distortion }.Where(k => w.Alpha(k) > 0);

    private static string DamageTypesText(CombatWeapon w)
    {
        var kinds = Kinds(w).Select(WeaponPenetration.FrenchName).ToList();
        return kinds.Count == 0 ? "?" : string.Join(" + ", kinds);
    }

    private static string ShortKind(DamageKind kind) => kind switch
    {
        DamageKind.Physical => "Phy",
        DamageKind.Energy => "Éne",
        DamageKind.Distortion => "Dis",
        _ => "?",
    };

    private static string Num(double value, int decimals = 1)
    {
        if (double.IsInfinity(value)) return "∞";
        var rounded = Math.Round(value, decimals);
        return rounded == Math.Floor(rounded) ? rounded.ToString("N0", Fr) : rounded.ToString("N" + decimals, Fr);
    }

    private static bool Contains(string? text, string query) =>
        text is not null && text.Contains(query, StringComparison.CurrentCultureIgnoreCase);

    private static Border Card()
    {
        var card = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 12, 14, 12) };
        card.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        return card;
    }

    private static TextBlock Muted(string text, double size, Thickness margin)
    {
        var block = new TextBlock { Text = text, FontSize = size, Margin = margin, TextWrapping = TextWrapping.Wrap };
        block.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        return block;
    }

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}
