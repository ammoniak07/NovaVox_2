using System.Runtime.InteropServices;
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
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    private IntPtr _hwnd;
    private bool _desiredClickThrough = true;
    private (int X, int Y)? _pendingPhysicalPosition;

    public Panel[] ColumnPanels => new Panel[] { Column0, Column1, Column2, Column3, Column4, Column5, Column6, Column7, Column8 };

    public OverlaySatelliteWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            // Rejoue l'état demandé avant que le HWND n'existe (voir
            // SetClickThrough/MoveToPhysical) : SourceInitialized ne s'est
            // déclenché qu'au premier Show(), qui peut survenir bien après
            // la construction (fenêtre créée pendant que l'overlay est
            // masqué, voir OverlayWindow.CreateSatelliteWindow).
            SetClickThrough(_desiredClickThrough);
            if (_pendingPhysicalPosition is { } pos) MoveToPhysical(pos.X, pos.Y);
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

    /// <summary>
    /// Repositionne en PIXELS PHYSIQUES D'ÉCRAN (comme PointToScreen —
    /// SetWindowPos, pas Window.Left/Top) — utilisée UNIQUEMENT par
    /// OverlayWindow.DetachRowToNewWindow, juste après la création, pour
    /// placer la fenêtre exactement là où la ligne a été lâchée. Remplace
    /// une conversion via Window.Left/Top qui nécessitait de diviser par le
    /// DPI d'une fenêtre de référence arbitraire (ex. la fenêtre
    /// principale) — correcte seulement si le glisser se déroulait sur ce
    /// même moniteur, décalée sinon dès que le curseur se trouvait sur un
    /// moniteur à mise à l'échelle différente (voir OverlayDragGhostWindow.
    /// MoveToPhysical pour le même correctif, plus de détails). La
    /// création elle-même (OverlayWindow.CreateSatelliteWindow) continue
    /// d'utiliser Left/Top en DIP pour restaurer une position déjà
    /// enregistrée (aucune conversion inter-moniteur là, juste relire ce
    /// qui a été écrit sous la même forme).
    /// </summary>
    public void MoveToPhysical(int screenX, int screenY)
    {
        if (_hwnd == IntPtr.Zero)
        {
            _pendingPhysicalPosition = (screenX, screenY);
            return;
        }
        SetWindowPos(_hwnd, IntPtr.Zero, screenX, screenY, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
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
