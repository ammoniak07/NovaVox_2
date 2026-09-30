using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace NovaVox.App.Overlay;

/// <summary>
/// Fenêtre jetable créée pour la durée d'UN glisser de ligne (voir
/// OverlayWindow.ShowDragGhost) — affiche un instantané de la ligne et suit
/// le curseur (MoveTo), fermée dès le relâchement ou une perte de capture
/// anormale (OverlayWindow.CloseDragGhost).
/// </summary>
public partial class OverlayDragGhostWindow : Window
{
    private IntPtr _hwnd;

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
        };
    }

    public void SetImage(BitmapSource image) => GhostImage.Source = image;

    public void MoveTo(double left, double top)
    {
        Left = left;
        Top = top;
    }
}
