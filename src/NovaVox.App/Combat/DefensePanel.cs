using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NovaVox.Core.Combat;

namespace NovaVox.App.Combat;

/// <summary>
/// Onglet « 🛡️ Défense » : on choisit son vaisseau et ses générateurs de
/// bouclier, et le tableau liste tous les vaisseaux du jeu (armes d'origine)
/// et mes vaisseaux ⭐ avec ce qu'ils font : temps pour faire tomber mon
/// bouclier, dégâts au blindage, ce qui passe à travers le bouclier.
/// Calcul : <see cref="DefenseCalculator"/>.
/// </summary>
public sealed class DefensePanel : Grid
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
    private static readonly Brush DangerBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0x4D, 0x5E)));
    private static readonly Brush WarnBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xB0, 0x20)));
    private static readonly Brush NeutralBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x40, 0x90, 0xA0, 0xB8)));
    private static readonly Brush SafeBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x50, 0x35, 0xE0, 0xA1)));

    private static readonly double[] ColumnWidths = { 0, 46, 90, 95, 85, 95, 95, 100, 90, 230 };

    private enum SortColumn { Name, Size, ShieldDps, ShieldTime, HullDps, ArmorTime, DestroyTime, Leak, Missiles, Verdict }

    private readonly MyShipsStore _store;
    private readonly ComboBox _shipCombo = new() { Margin = new Thickness(0, 4, 0, 10) };
    private readonly StackPanel _shieldRows = new();
    private readonly StackPanel _summaryLeft = new() { Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBox _search = new() { Width = 200, ToolTip = "Rechercher un attaquant" };
    private readonly ComboBox _showCombo = new() { Width = 280, Margin = new Thickness(10, 0, 0, 0) };
    private readonly TextBlock _summary = new() { FontSize = 13, Margin = new Thickness(0, 8, 0, 8), TextWrapping = TextWrapping.Wrap };
    private readonly Grid _header = new() { Margin = new Thickness(0, 0, 14, 4) };
    private readonly StackPanel _rows = new();

    /// <summary>Choix de boucliers en cours (copie de travail : enregistrée seulement pour un vaisseau ⭐).</summary>
    private MyShipLoadout? _working;
    private MyShipLoadout? _saved;
    private CombatShip? _ship;
    private SortColumn _sort = SortColumn.Verdict;
    private bool _sortDescending;
    private bool _loading;

    public DefensePanel(MyShipsStore store)
    {
        _store = store;
        Margin = new Thickness(14);
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = BuildLeft();
        SetColumn(left, 0);
        Children.Add(left);
        var right = BuildRight();
        SetColumn(right, 2);
        Children.Add(right);

        foreach (var label in new[] { "Tous les attaquants", "Seulement ceux qui percent mon blindage", "Seulement ceux qui ne le percent pas" })
            _showCombo.Items.Add(new ComboBoxItem { Content = label });
        _showCombo.SelectedIndex = 0;
        FillShipCombo(null);
    }

    // ───────────────────────────── Mon vaisseau ─────────────────────────────

    private UIElement BuildLeft()
    {
        var card = Ui.Card();
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = "🛡️ MON VAISSEAU", FontSize = 18, FontWeight = FontWeights.Bold });

        var header = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var edit = new Button { Content = "🛠 Mes vaisseaux…", Padding = new Thickness(8, 2, 8, 2), FontSize = 12, ToolTip = "Crée tes vaisseaux et choisis leurs armes et boucliers" };
        edit.Click += (_, _) => OpenMyShips();
        DockPanel.SetDock(edit, Dock.Right);
        header.Children.Add(edit);
        header.Children.Add(Ui.Muted("Vaisseau à défendre", 12, new Thickness(0, 0, 8, 0)));
        stack.Children.Add(header);
        stack.Children.Add(_shipCombo);

        stack.Children.Add(Ui.Section("Générateurs de bouclier"));
        stack.Children.Add(_shieldRows);
        stack.Children.Add(_summaryLeft);

        _shipCombo.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            SelectShip((_shipCombo.SelectedItem as ComboBoxItem)?.Tag);
        };

        card.Child = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        return card;
    }

    private void FillShipCombo(string? selectLoadout)
    {
        _loading = true;
        _shipCombo.Items.Clear();
        ComboBoxItem? toSelect = null;
        foreach (var loadout in _store.Load())
        {
            var item = new ComboBoxItem { Content = $"⭐ {loadout.Name}  ({loadout.Ship})", Tag = loadout };
            _shipCombo.Items.Add(item);
            if (loadout.Name == selectLoadout) toSelect = item;
        }
        foreach (var ship in CombatDatabase.Ships.OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase))
            _shipCombo.Items.Add(new ComboBoxItem { Content = $"{ship.Name}  (d'origine)", Tag = ship });
        toSelect ??= _shipCombo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag is CombatShip { Name: "Cutlass Black" })
                     ?? _shipCombo.Items.OfType<ComboBoxItem>().FirstOrDefault();
        _shipCombo.SelectedItem = toSelect;
        _loading = false;
        SelectShip(toSelect?.Tag);
    }

    private void OpenMyShips()
    {
        var window = new MyShipsWindow(_store, _saved?.Name) { Owner = Window.GetWindow(this) };
        window.ShowDialog();
        FillShipCombo(window.Selected?.Name ?? _saved?.Name);
    }

    private void SelectShip(object? tag)
    {
        switch (tag)
        {
            case MyShipLoadout loadout:
                _saved = loadout;
                _ship = CombatDatabase.FindShip(loadout.Ship);
                _working = loadout;
                break;
            case CombatShip ship:
                _saved = null;
                _ship = ship;
                _working = new MyShipLoadout { Name = ship.Name, Ship = ship.Name };
                break;
            default:
                _saved = null;
                _ship = null;
                _working = null;
                break;
        }
        BuildShieldRows();
        Refresh();
    }

    private void BuildShieldRows()
    {
        _shieldRows.Children.Clear();
        if (_ship is null || _working is null) return;
        var mounts = _ship.MountList.Where(m => m.IsShield).ToList();
        if (mounts.Count == 0)
        {
            _shieldRows.Children.Add(Ui.Muted("Pas d'emplacement de bouclier connu : bouclier d'origine.", 12, default));
            return;
        }
        foreach (var mount in mounts)
        {
            var combo = new ComboBox { Margin = new Thickness(0, 2, 0, 6), IsEnabled = mount.Editable };
            combo.Items.Add(new ComboBoxItem { Content = "— Vide —", Tag = "" });
            foreach (var shield in CombatDatabase.ShieldsFor(mount))
            {
                var stock = string.Equals(shield.Name, mount.Stock, StringComparison.OrdinalIgnoreCase) ? "  (d'origine)" : "";
                var grade = string.Join(" ", new[] { shield.Class, shield.Grade }.Where(s => !string.IsNullOrWhiteSpace(s)));
                combo.Items.Add(new ComboBoxItem
                {
                    Content = $"S{shield.Size} · {shield.Name} — {Num(shield.Hp, 0)} PV{(grade.Length > 0 ? " · " + grade : "")}{stock}",
                    Tag = shield.Name,
                });
            }
            var current = _working.WeaponOn(mount) ?? "";
            combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => string.Equals((string)i.Tag, current, StringComparison.OrdinalIgnoreCase))
                                 ?? combo.Items[0];
            combo.SelectionChanged += (_, _) =>
            {
                if (_working is null || combo.SelectedItem is not ComboBoxItem { Tag: string name }) return;
                if (string.Equals(name, mount.Stock ?? "", StringComparison.OrdinalIgnoreCase)) _working.Weapons.Remove(mount.Id);
                else _working.Weapons[mount.Id] = name;
                if (_saved is not null) SaveLoadout();
                Refresh();
            };
            _shieldRows.Children.Add(Ui.Muted($"{mount.FrenchLabel} (S{mount.MaxSize})", 12, new Thickness(0, 4, 0, 0)));
            _shieldRows.Children.Add(combo);
        }
        if (_saved is null)
            _shieldRows.Children.Add(Ui.Muted("Pour garder tes choix, crée ce vaisseau dans « 🛠 Mes vaisseaux… ».", 11, new Thickness(0, 2, 0, 0)));
    }

    private void SaveLoadout()
    {
        if (_saved is null) return;
        var all = _store.Load();
        var index = all.FindIndex(l => l.Name == _saved.Name);
        if (index < 0) return;
        all[index] = _saved;
        try
        {
            _store.Save(all);
        }
        catch
        {
            // Enregistrement impossible (disque) : les choix restent appliqués à l'écran.
        }
    }

    private void FillLeftSummary(DefenseProfile profile)
    {
        _summaryLeft.Children.Clear();
        var ship = profile.Ship;
        _summaryLeft.Children.Add(Ui.Section("Ma défense"));
        Line("Déflexion du blindage", $"{Num(ship.DeflectionOf(DamageKind.Physical), 1)} physique · {Num(ship.DeflectionOf(DamageKind.Energy), 1)} énergie · {Num(ship.DeflectionOf(DamageKind.Distortion), 1)} distorsion");
        Line("Blindage", $"{Num(ship.ArmorHp, 0)} PV");
        Line("Structure (coque)", ship.HullHp > 0 ? $"{Num(ship.HullHp, 0)} PV, à détruire après le blindage" : "?");
        var activeText = profile.Mounted.Count > profile.Active.Count ? $" ({profile.Active.Count} générateurs actifs sur {profile.Mounted.Count})" : "";
        Line("Bouclier", $"{Num(profile.ShieldHp, 0)} PV{activeText}");
        Line("Régénération", $"{Num(profile.ShieldRegen, 0)} PV/s");
        if (profile.Active.Count > 0)
        {
            Line("Reprise après un coup", $"{Num(profile.Active.Max(s => s.DamagedDelay), 1)} s");
            Line("Remonte après être tombé", $"{Num(profile.Active.Max(s => s.DownedDelay), 1)} s");
        }
        _summaryLeft.Children.Add(Ui.Muted(
            "Le bouclier absorbe toute l'énergie et la distorsion, et 45 % du physique quand il est plein. " +
            "Seuls les 2 générateurs les plus solides fonctionnent en même temps.", 11, new Thickness(0, 8, 0, 0)));
    }

    private void Line(string label, string value)
    {
        var grid = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(Ui.Muted(label, 12, default));
        var v = new TextBlock { Text = value, FontSize = 12, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        SetColumn(v, 1);
        grid.Children.Add(v);
        _summaryLeft.Children.Add(grid);
    }

    // ───────────────────────────── Les attaquants ─────────────────────────────

    private UIElement BuildRight()
    {
        var card = Ui.Card();
        var grid = new Grid();
        for (var i = 0; i < 3; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var top = new DockPanel();
        var tools = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        tools.Children.Add(Ui.Muted("🔍", 13, new Thickness(0, 0, 6, 0)));
        tools.Children.Add(_search);
        tools.Children.Add(_showCombo);
        DockPanel.SetDock(tools, Dock.Right);
        top.Children.Add(tools);
        top.Children.Add(new TextBlock { Text = "⚔️ QUI PEUT ME FAIRE DES DÉGÂTS", FontSize = 18, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
        grid.Children.Add(top);

        SetRow(_summary, 1);
        grid.Children.Add(_summary);

        BuildHeader();
        SetRow(_header, 2);
        grid.Children.Add(_header);

        var scroll = new ScrollViewer { Content = _rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        SetRow(scroll, 3);
        grid.Children.Add(scroll);

        var help = Ui.Muted(
            "Chaque attaquant avec toutes ses armes d'origine (tourelles comprises), ou tes vaisseaux ⭐ avec les armes choisies. " +
            "Estimations, tous les tirs touchent : le bouclier ne régénère pas sous le feu. « Coque » = blindage puis structure, détruire le vaisseau demande de vider les deux. " +
            "Rouge : ses canons percent ton blindage (ou sa salve de missiles suffit à te détruire) ; gris : seulement ses missiles (une salve, % de ta coque) ou de la distorsion (composants, pas de dégâts de coque) ; " +
            "orange : perce même à travers ton bouclier plein (balistique) ; vert : ne fait que vider ton bouclier, ses tirs ricochent ensuite. " +
            "Survole une ligne pour voir le détail de ses armes. Données du jeu " + CombatDatabase.GameVersion + ".",
            11, new Thickness(0, 8, 0, 0));
        SetRow(help, 4);
        grid.Children.Add(help);

        _search.TextChanged += (_, _) => Refresh();
        _showCombo.SelectionChanged += (_, _) => Refresh();

        card.Child = grid;
        return card;
    }

    private void BuildHeader()
    {
        _header.Children.Clear();
        _header.ColumnDefinitions.Clear();
        AddColumns(_header);
        var titles = new (string Text, SortColumn Column, string Tip)[]
        {
            ("Attaquant", SortColumn.Name, "Vaisseau attaquant"),
            ("Taille", SortColumn.Size, "Taille du vaisseau attaquant"),
            ("DPS bouclier", SortColumn.ShieldDps, "PV de ton bouclier retirés par seconde"),
            ("Bouclier tombe", SortColumn.ShieldTime, "Temps pour faire tomber ton bouclier"),
            ("DPS coque", SortColumn.HullDps, "Dégâts par seconde à ton blindage puis à ta structure une fois le bouclier tombé (canons qui dépassent ta déflexion ; la distorsion ne compte pas)"),
            ("Blindage vidé", SortColumn.ArmorTime, "Temps pour vider ton blindage une fois le bouclier tombé"),
            ("Détruit en", SortColumn.DestroyTime, "Temps pour te détruire avec ses canons : bouclier, puis blindage, puis structure"),
            ("Bouclier levé", SortColumn.Leak, "Dégâts par seconde à ta coque même avec ton bouclier plein (part physique qui traverse le bouclier et dépasse ta déflexion)"),
            ("Missiles", SortColumn.Missiles, "Dégâts de toute sa salve de missiles/torpilles à ta coque (une fois ton bouclier tombé)"),
            ("Verdict", SortColumn.Verdict, "Du plus dangereux au moins dangereux"),
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
                    _sortDescending = column is SortColumn.ShieldDps or SortColumn.HullDps or SortColumn.Leak or SortColumn.Missiles or SortColumn.Size;
                }
                BuildHeader();
                Refresh();
            };
            SetColumn(label, i);
            _header.Children.Add(label);
        }
    }

    private static void AddColumns(Grid grid)
    {
        foreach (var width in ColumnWidths)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width <= 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(width) });
    }

    private void Refresh()
    {
        _rows.Children.Clear();
        if (_ship is null || _working is null)
        {
            _summary.Text = "Choisis ton vaisseau à gauche.";
            _summaryLeft.Children.Clear();
            return;
        }

        var profile = DefenseCalculator.Profile(_ship, _working);
        FillLeftSummary(profile);

        var attacks = new List<AttackOnMe>();
        foreach (var mine in _store.Load())
        {
            if (CombatDatabase.FindShip(mine.Ship) is not { } model) continue;
            var weapons = mine.EquippedWeapons(model);
            if (weapons.Count > 0) attacks.Add(DefenseCalculator.Evaluate(profile, "⭐ " + mine.Name, model, weapons));
        }
        attacks.AddRange(DefenseCalculator.AllStockAttackers(profile));

        var guns = attacks.Count(a => a.GunsPierce);
        var leak = attacks.Count(a => a.LeakHullDps > 0);
        var missilesOnly = attacks.Count(a => !a.GunsPierce && a.MissileHullDamage > 0);
        var safe = attacks.Count - guns - missilesOnly;
        _summary.Text = $"{_ship.Name} : sur {attacks.Count} attaquants, {guns} percent ton blindage avec leurs canons (dont {leak} même bouclier levé), " +
                        $"{missilesOnly} seulement avec leurs missiles (une salve), et {safe} ne font que vider ton bouclier.";

        var query = _search.Text.Trim();
        IEnumerable<AttackOnMe> shown = attacks;
        if (query.Length > 0)
            shown = shown.Where(a => a.Attacker.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                                     || (a.AttackerShip?.FullName.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false));
        shown = _showCombo.SelectedIndex switch
        {
            1 => shown.Where(a => a.CanHurtHull),
            2 => shown.Where(a => !a.CanHurtHull),
            _ => shown,
        };

        var index = 0;
        foreach (var attack in Sort(shown))
            _rows.Children.Add(BuildRow(attack, index++));
        if (index == 0) _rows.Children.Add(Ui.Muted("Aucun attaquant ne correspond.", 12, new Thickness(8)));
    }

    private IEnumerable<AttackOnMe> Sort(IEnumerable<AttackOnMe> attacks)
    {
        if (_sort == SortColumn.Name)
            return _sortDescending
                ? attacks.OrderByDescending(a => a.Attacker, StringComparer.CurrentCultureIgnoreCase)
                : attacks.OrderBy(a => a.Attacker, StringComparer.CurrentCultureIgnoreCase);
        Func<AttackOnMe, double> key = _sort switch
        {
            SortColumn.Size => a => a.AttackerShip?.Size ?? 0,
            SortColumn.ShieldDps => a => a.ShieldDps,
            SortColumn.ShieldTime => a => Cap(a.SecondsToDropShield),
            SortColumn.HullDps => a => a.HullDps,
            SortColumn.ArmorTime => a => Cap(a.SecondsToStripArmor),
            SortColumn.DestroyTime => a => Cap(a.SecondsToDestroy),
            SortColumn.Leak => a => a.LeakHullDps,
            SortColumn.Missiles => a => a.MissileHullDamage,
            // Les plus dangereux d'abord : touche à travers le bouclier, puis perce
            // le blindage avec ses canons (le plus rapide d'abord), puis missiles
            // seulement (la plus grosse salve d'abord), puis ceux qui ricochent.
            // Les plus dangereux d'abord : ceux qui te détruisent le plus vite avec
            // leurs canons (bouclier levé ou non), puis missiles, puis les autres.
            _ => a => a.HullDps > 0 || a.LeakHullDps > 0 ? Cap(a.SecondsToDestroy) / 1e9
                : a.MissileShareOfHull >= 1 ? 2 + 1 / (1 + a.MissileHullDamage)
                : a.MissileHullDamage > 0 ? 3 + 1 / (1 + a.MissileHullDamage)
                : 4 + Cap(a.SecondsToDropShield) / 1e9,
        };
        return _sortDescending
            ? attacks.OrderByDescending(key).ThenBy(a => a.Attacker, StringComparer.CurrentCultureIgnoreCase)
            : attacks.OrderBy(key).ThenBy(a => a.Attacker, StringComparer.CurrentCultureIgnoreCase);
    }

    private static double Cap(double seconds) => double.IsInfinity(seconds) ? 1e8 : seconds;

    private UIElement BuildRow(AttackOnMe a, int index)
    {
        var row = new Grid { MinHeight = 26 };
        AddColumns(row);
        var border = new Border { Child = row, Padding = new Thickness(0, 2, 0, 2), ToolTip = WeaponsTooltip(a) };
        if (index % 2 == 1) border.SetResourceReference(Border.BackgroundProperty, "PanelAltBrush50");

        AddCell(row, 0, a.Attacker, null, bold: true);
        AddCell(row, 1, a.AttackerShip?.Size is { } size and > 0 ? $"S{size}" : "—", null);
        AddCell(row, 2, Num(a.ShieldDps, 0), null);
        AddCell(row, 3, Duration(a.SecondsToDropShield), null);
        AddCell(row, 4, a.HullDps > 0 ? Num(a.HullDps, 0) : "0", a.HullDps > 0 ? DangerBrush : null);
        AddCell(row, 5, Duration(a.SecondsToStripArmor), null);
        AddCell(row, 6, Duration(a.SecondsToDestroy), a.HullDps > 0 || a.LeakHullDps > 0 ? DangerBrush : null);
        AddCell(row, 7, a.LeakHullDps > 0 ? $"⚠ {Num(a.LeakHullDps, 0)}/s" : "rien", a.LeakHullDps > 0 ? WarnBrush : null);
        AddCell(row, 8, a.MissileHullDamage > 0 ? Num(a.MissileHullDamage, 0) : "rien", null);

        var (text, brush) = Verdict(a);
        AddCell(row, 9, text, brush);
        return border;
    }

    /// <summary>
    /// Verdict d'un attaquant. Les missiles sont un cas à part : une salve
    /// unique, sans temps « pour vider le blindage » (contrairement aux canons).
    /// </summary>
    private static (string Text, Brush Brush) Verdict(AttackOnMe a)
    {
        if (a.LeakHullDps > 0) return ($"⚠ Te touche même bouclier levé", WarnBrush);
        if (a.HullDps > 0) return ($"✖ Ses canons percent ton blindage", DangerBrush);
        if (a.MissileHullDamage > 0)
        {
            var share = a.MissileShareOfHull;
            return share >= 1
                ? ("✖ Sa salve de missiles peut te détruire", DangerBrush)
                : ($"◇ Missiles seulement : {Num(share * 100, share < 0.1 ? 1 : 0)} % de ta coque", NeutralBrush);
        }
        if (a.DistortionDps > 0) return ("◇ Distorsion : surcharge tes composants", NeutralBrush);
        return ("✔ Ricoche sur ton blindage", SafeBrush);
    }

    private static string WeaponsTooltip(AttackOnMe a)
    {
        var lines = new List<string> { a.AttackerShip?.FullName ?? a.Attacker };
        foreach (var t in a.Weapons)
        {
            var mark = t.PenetratesThroughShield ? "⚠" : t.PenetratesArmor ? "✖" : "✔";
            var verdict = t.PenetratesThroughShield ? "perce même à travers le bouclier" : t.PenetratesArmor ? "perce le blindage" : "ricoche sur le blindage";
            var damage = string.Join(" + ", new[] { DamageKind.Physical, DamageKind.Energy, DamageKind.Distortion }
                .Where(k => t.Weapon.Alpha(k) > 0)
                .Select(k => $"{Num(t.Weapon.Alpha(k), 1)} {WeaponPenetration.FrenchName(k).ToLowerInvariant()}"));
            lines.Add($"{mark} {(t.Count > 0 ? t.Count + "× " : "")}S{t.Weapon.Size} {t.Weapon.Name} ({damage}) : {verdict}");
        }
        return string.Join("\n", lines);
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
                ToolTip = text.Length > 18 ? text : null,
            },
        };
        SetColumn(cell, column);
        row.Children.Add(cell);
    }

    private static string Duration(double seconds)
    {
        if (double.IsInfinity(seconds) || seconds > 3600 * 10) return "jamais";
        if (seconds < 60) return $"{Num(seconds, seconds < 10 ? 1 : 0)} s";
        var minutes = (int)(seconds / 60);
        return $"{minutes} min {(int)(seconds % 60):00}";
    }

    private static string Num(double value, int decimals)
    {
        if (double.IsInfinity(value)) return "∞";
        var rounded = Math.Round(value, decimals);
        return rounded == Math.Floor(rounded) ? rounded.ToString("N0", Fr) : rounded.ToString("N" + decimals, Fr);
    }

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}
