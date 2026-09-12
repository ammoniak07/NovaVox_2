using System.Runtime.InteropServices;

namespace NovaVox.App.Overlay;

/// <summary>
/// Force la barre de titre native (celle dessinée par Windows, PAS le
/// contenu de la page) en mode sombre plutôt que le blanc par défaut, qui
/// jurait avec le thème sombre de l'application — port de
/// _apply_dark_titlebar (app.py), via l'API DWM (Desktop Window Manager).
/// </summary>
internal static class DarkTitleBar
{
    // Valeur documentée par Microsoft (DWMWINDOWATTRIBUTE), supportée
    // depuis Windows 10 1809 (build 17763) et Windows 11.
    private const int DwmwaUseImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    /// <summary>
    /// Silencieux en cas d'échec (Windows antérieur à 2018, ou hwnd
    /// introuvable) : la fenêtre reste utilisable avec la barre de titre
    /// blanche par défaut dans ce cas, ce réglage est purement cosmétique.
    /// </summary>
    public static void Apply(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        try
        {
            var value = 1;
            DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref value, sizeof(int));
        }
        catch
        {
            // Best effort, purement cosmétique.
        }
    }
}
