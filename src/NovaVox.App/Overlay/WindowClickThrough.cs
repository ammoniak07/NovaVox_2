using System.Runtime.InteropServices;

namespace NovaVox.App.Overlay;

/// <summary>
/// Active/désactive le mode "clic-traversant" d'une fenêtre déjà créée —
/// port de _set_window_clickthrough (app.py). Ne touche QUE le bit
/// WS_EX_TRANSPARENT : WPF gère déjà lui-même la transparence par pixel
/// de la fenêtre (AllowsTransparency="True" sur OverlayWindow), pas
/// besoin d'y toucher ici — seul WS_EX_TRANSPARENT suffit pour faire
/// passer les clics/la souris jusqu'au jeu en dessous.
/// </summary>
internal static class WindowClickThrough
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : (IntPtr)GetWindowLong32(hWnd, nIndex);

    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong) =>
        IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong) : (IntPtr)SetWindowLong32(hWnd, nIndex, (int)dwNewLong);

    public static void SetClickThrough(IntPtr hwnd, bool clickThrough)
    {
        if (hwnd == IntPtr.Zero) return;
        long styleBefore = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        long styleAfter = clickThrough ? (styleBefore | WsExTransparent) : (styleBefore & ~WsExTransparent);
        SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(styleAfter));
    }
}
