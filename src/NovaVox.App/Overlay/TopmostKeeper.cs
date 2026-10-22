using System.Runtime.InteropServices;

namespace NovaVox.App.Overlay;

/// <summary>
/// Remet une fenêtre de l'overlay tout en haut de la pile des fenêtres
/// "toujours au premier plan". Topmost="True" ne suffit pas toujours : en
/// revenant dans Star Citizen après un Alt+Tab, le jeu peut repasser
/// devant l'overlay (remontée utilisateur, 22/10/2026). Il fallait alors
/// désactiver puis réactiver l'overlay à la main.
/// </summary>
internal static class TopmostKeeper
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoOwnerZOrder = 0x0200;

    /// <summary>Sans déplacer, redimensionner ni activer la fenêtre (le jeu garde le focus clavier/souris).</summary>
    public static void Reassert(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpNoOwnerZOrder);
    }

    // ------------------------------------------------ changement de fenêtre active

    public delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    private const uint EventSystemForeground = 0x0003;
    private const uint WinEventOutOfContext = 0x0000;

    /// <summary>
    /// Appelle <paramref name="callback"/> à chaque fois qu'une autre
    /// fenêtre passe au premier plan (Alt+Tab, clic...), sur le thread qui
    /// installe le crochet (thread UI). Le délégué doit rester référencé
    /// par l'appelant tant que le crochet est actif.
    /// </summary>
    public static IntPtr HookForegroundChanges(WinEventDelegate callback) =>
        SetWinEventHook(EventSystemForeground, EventSystemForeground, IntPtr.Zero, callback, 0, 0, WinEventOutOfContext);

    public static void Unhook(IntPtr hook)
    {
        if (hook != IntPtr.Zero) UnhookWinEvent(hook);
    }
}
