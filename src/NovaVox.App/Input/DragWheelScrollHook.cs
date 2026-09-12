using System.Runtime.InteropServices;

namespace NovaVox.App.Input;

/// <summary>
/// Pendant un DragDrop.DoDragDrop (boucle OLE modale), WPF ne délivre plus
/// les événements de molette (MouseWheel) normaux au thread UI — impossible
/// de faire défiler une ListBox pendant qu'on y glisse un élément avec la
/// souris. Contourne ça avec un crochet souris bas niveau (WH_MOUSE_LL),
/// qui lui continue de recevoir les notifications de molette même pendant
/// cette boucle modale (contournement standard pour cette limitation connue
/// de WPF/OLE) — actif seulement le temps du glisser (installé juste avant
/// DoDragDrop, retiré juste après, voir CommandsList_PreviewMouseMove).
/// </summary>
internal sealed class DragWheelScrollHook : IDisposable
{
    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    private const int WH_MOUSE_LL = 14;
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
    private readonly Action<int> _onWheelDelta;
    private IntPtr _hookHandle;

    /// <param name="onWheelDelta">Reçoit le delta de molette signé (±120 par cran, comme WM_MOUSEWHEEL), appelé sur le thread UI.</param>
    public DragWheelScrollHook(Action<int> onWheelDelta)
    {
        _onWheelDelta = onWheelDelta;
        _proc = HookCallback;
        _hookHandle = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (int)wParam == WM_MOUSEWHEEL)
        {
            var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            var delta = (short)((data.MouseData >> 16) & 0xffff);
            _onWheelDelta(delta);
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
