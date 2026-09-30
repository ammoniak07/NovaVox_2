using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using NovaVox.Core.Config;

namespace NovaVox.App.Overlay;

/// <summary>
/// Fenêtre "satellite" créée en glissant une ligne hors de l'overlay
/// principal (voir OverlayWindow._windows/DetachRowToNewWindow) — chrome
/// identique à OverlayWindow (transparence, coins arrondis, ombre), mais
/// SANS aucune logique de glisser-déposer propre : les gestionnaires
/// d'évènements des poignées de ligne (RowDragHandle_*) restent ceux de
/// l'unique instance OverlayWindow "principale" qui les a créés (un
/// abonnement à un évènement C# survit au déplacement de son élément vers
/// une autre fenêtre) — cette classe n'a donc qu'à exposer son apparence
/// (ApplyBackground/ApplyScale/SetEditModeBorder) et son clic-traversant
/// (SetClickThrough), pilotés depuis OverlayWindow exactement comme ses
/// propres réglages.
/// </summary>
public partial class OverlaySatelliteWindow : Window
{
    private IntPtr _hwnd;
    private bool _desiredClickThrough = true;

    public Panel[] ColumnPanels => new Panel[] { Column0, Column1, Column2, Column3, Column4, Column5, Column6, Column7, Column8 };

    public OverlaySatelliteWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            // Rejoue l'état demandé avant que le HWND n'existe (voir
            // SetClickThrough) : SourceInitialized ne s'est déclenché qu'au
            // premier Show(), qui peut survenir bien après la construction
            // (fenêtre créée pendant que l'overlay est masqué, voir
            // OverlayWindow.CreateSatelliteWindow).
            SetClickThrough(_desiredClickThrough);
        };
    }

    /// <summary>
    /// Mémorise toujours la valeur demandée (_desiredClickThrough), même
    /// avant que le HWND n'existe — appliquée immédiatement si possible,
    /// sinon rejouée dès SourceInitialized (voir le constructeur).
    /// </summary>
    public void SetClickThrough(bool clickThrough)
    {
        _desiredClickThrough = clickThrough;
        if (_hwnd != IntPtr.Zero) WindowClickThrough.SetClickThrough(_hwnd, clickThrough);
    }

    /// <summary>Bordure cyan vive en mode édition, discrète sinon — même palette que OverlayWindow.RefreshBorderColor (sans le cadre rouge d'alerte micro, propre à la fenêtre principale).</summary>
    public void SetEditModeBorder(bool editing)
    {
        PanelBorder.BorderBrush = editing
            ? new SolidColorBrush(Color.FromRgb(0x2D, 0xD4, 0xFF))
            : new SolidColorBrush(Color.FromArgb(0x59, 0x2D, 0xD4, 0xFF));
    }

    /// <summary>Suit toujours la même couleur/opacité de fond que l'overlay principal (un seul réglage global, pas de personnalisation par fenêtre détachée) — voir OverlayWindow.ApplyAppearance.</summary>
    public void ApplyBackground(string bgColorHex, int bgOpacityPercent)
    {
        var bgColor = (Color)ColorConverter.ConvertFromString(bgColorHex)!;
        bgColor.A = (byte)Math.Clamp(bgOpacityPercent * 255 / 100, 0, 255);
        PanelBorder.Background = new SolidColorBrush(bgColor);
    }

    /// <summary>Suit toujours la même échelle que l'overlay principal — voir OverlayWindow.ApplyScale.</summary>
    public void ApplyScale(double scale)
    {
        if (scale < OverlayConfig.MinScale || scale > OverlayConfig.MaxScale) return;
        OverlayScaleTransform.ScaleX = scale;
        OverlayScaleTransform.ScaleY = scale;
    }
}
