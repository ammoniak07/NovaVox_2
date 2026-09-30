using System.Runtime.InteropServices;

namespace NovaVox.App.Input;

/// <summary>
/// Pendant un DragDrop.DoDragDrop (boucle OLE modale), WPF ne délivre plus
/// les événements de molette (MouseWheel) NI de déplacement (MouseMove)
/// normaux au thread UI — impossible de faire défiler une ListBox pendant
/// qu'on y glisse un élément avec la souris, ni de savoir où se trouve le
/// curseur pour y faire suivre un fantôme (voir MainWindow.
/// ShowCommandDragGhost/MoveCommandDragGhostTo, même principe que le
/// fantôme de l'overlay — OverlayWindow.ShowDragGhost — mais nécessitant
/// ici ce contournement puisque DoDragDrop, contrairement à la capture
/// souris manuelle utilisée côté overlay, ne délivre plus aucun évènement
/// souris WPF normal pendant la boucle modale). Contourne ça avec un
/// crochet souris bas niveau (WH_MOUSE_LL), qui lui continue de recevoir
/// TOUTES les notifications souris même pendant cette boucle modale
/// (contournement standard pour cette limitation connue de WPF/OLE) — actif
/// seulement le temps du glisser (installé juste avant DoDragDrop, retiré
/// juste après, voir CommandsList_PreviewMouseMove). Le rappel s'exécute
/// sur le thread qui a installé le crochet (celui de l'UI ici), donc sans
/// besoin de Dispatcher.Invoke pour toucher des objets WPF depuis
/// _onWheelDelta/_onMouseMove.
/// </summary>
internal sealed class DragWheelScrollHook : IDisposable
{
    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    private const int WH_MOUSE_LL = 14;
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_MOUSEWHEEL = 0x020A;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT Pt;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    // Gardé en champ : un délégué passé à SetWindowsHookEx sans référence
    // conservée côté managé peut être ramassé par le GC pendant que le
    // crochet natif s'en sert encore, ce qui plante au prochain appel.
    private readonly LowLevelMouseProc _proc;
    private readonly Action<int>? _onWheelDelta;
    private readonly Action<int, int>? _onMouseMove;
    private IntPtr _hookHandle;

    /// <param name="onWheelDelta">Reçoit le delta de molette signé (±120 par cran, comme WM_MOUSEWHEEL), appelé sur le thread UI.</param>
    /// <param name="onMouseMove">Reçoit la position ÉCRAN (pixels physiques, comme GetCursorPos/PointToScreen) du curseur à chaque déplacement, appelé sur le thread UI — voir MainWindow.MoveCommandDragGhostTo.</param>
    public DragWheelScrollHook(Action<int>? onWheelDelta = null, Action<int, int>? onMouseMove = null)
    {
        _onWheelDelta = onWheelDelta;
        _onMouseMove = onMouseMove;
        _proc = HookCallback;
        _hookHandle = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var msg = (int)wParam;
            if (msg == WM_MOUSEWHEEL && _onWheelDelta is not null)
            {
                var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                var delta = (short)((data.MouseData >> 16) & 0xffff);
                _onWheelDelta(delta);
            }
            else if (msg == WM_MOUSEMOVE && _onMouseMove is not null)
            {
                var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                _onMouseMove(data.Pt.X, data.Pt.Y);
            }
        }
        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }
}
