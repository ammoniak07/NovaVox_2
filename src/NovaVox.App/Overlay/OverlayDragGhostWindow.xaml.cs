using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace NovaVox.App.Overlay;

/// <summary>
/// Fenêtre jetable créée pour la durée d'UN glisser de ligne (voir
/// OverlayWindow.ShowDragGhost/MainWindow.ShowCommandDragGhost — réutilisée
/// telle quelle pour les deux) — affiche un instantané de la ligne et suit
/// le curseur (MoveToPhysical), fermée dès le relâchement ou une perte de
/// capture anormale (CloseDragGhost).
/// </summary>
public partial class OverlayDragGhostWindow : Window
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    private IntPtr _hwnd;
    private (int X, int Y)? _pendingPhysicalPosition;

    public OverlayDragGhostWindow()
    {
        InitializeComponent();
        WindowStartupLocation = WindowStartupLocation.Manual;
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            // Clic-traversant dès l'apparition, jamais réglable autrement :
            // ce n'est qu'un aperçu visuel (voir IsHitTestVisible="False"
            // côté XAML, qui ne suffit qu'à éviter la capture WPF interne —
            // WS_EX_TRANSPARENT est ce qui empêche réellement Windows de lui
            // envoyer le moindre clic/survol).
            WindowClickThrough.SetClickThrough(_hwnd, clickThrough: true);
            if (_pendingPhysicalPosition is { } pos) MoveToPhysical(pos.X, pos.Y);
        };
    }

    public void SetImage(BitmapSource image) => GhostImage.Source = image;

    /// <summary>
    /// Positionne la fenêtre en PIXELS PHYSIQUES D'ÉCRAN — les mêmes unités
    /// que PointToScreen ou le crochet souris bas niveau (voir
    /// DragWheelScrollHook) fournissent déjà, SANS aucune conversion DPI à
    /// faire ici. Remplace l'ancien MoveTo(double,double) qui positionnait
    /// via Window.Left/Top (unités WPF indépendantes de la résolution) après
    /// avoir divisé par le DPI d'UNE fenêtre de référence arbitraire (ex.
    /// la fenêtre principale) — correct UNIQUEMENT si le glisser se
    /// déroulait sur CE moniteur précis, et donc décalé dès que le curseur
    /// se trouvait sur un autre moniteur à mise à l'échelle différente (ex.
    /// 125% sur l'écran principal, 100% sur le second — signalé en
    /// conditions réelles). SetWindowPos travaille directement en pixels
    /// physiques du bureau virtuel entier, sans notion de "quel moniteur"
    /// à deviner. Mémorise la position si le HWND n'existe pas encore
    /// (avant le premier Show()) pour l'appliquer dès SourceInitialized,
    /// sans passer par Left/Top (qui resterait, lui, sujet au même problème).
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
}
