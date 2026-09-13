using System.Runtime.InteropServices;
using System.Windows.Media;
using NovaVox.Core;

namespace NovaVox.App.Overlay;

/// <summary>
/// Force la barre de titre native (celle dessinée par Windows, PAS le
/// contenu de la page) en mode sombre plutôt que le blanc par défaut, qui
/// jurait avec le thème sombre de l'application — port de
/// _apply_dark_titlebar (app.py), via l'API DWM (Desktop Window Manager).
/// </summary>
internal static class DarkTitleBar
{
    // Valeurs documentées par Microsoft (DWMWINDOWATTRIBUTE).
    private const int DwmwaUseImmersiveDarkMode = 20; // Windows 10 1809+ et Windows 11 : bascule juste sombre/clair, pas de couleur précise.
    private const int DwmwaCaptionColor = 35; // Windows 11 22000+ seulement : couleur exacte de la barre de titre.

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
        catch (Exception ex)
        {
            AppLog.Append(NovaVoxPaths.BaseDirectory, $"[Fenêtre] Barre de titre sombre indisponible (Windows trop ancien ?) : {ex.Message}.", "diagnostic");
        }
    }

    /// <summary>
    /// Colore la barre de titre exactement comme le reste de l'interface
    /// (au lieu du gris générique du mode sombre de Windows) — Windows 11
    /// (build 22000+) seulement ; sans effet silencieux sur Windows 10, qui
    /// garde la barre de titre sombre générique posée par <see cref="Apply"/>.
    /// </summary>
    public static void ApplyCaptionColor(IntPtr hwnd, Color color)
    {
        if (hwnd == IntPtr.Zero) return;
        try
        {
            // COLORREF : 0x00BBGGRR, pas 0x00RRGGBB.
            var colorRef = (color.B << 16) | (color.G << 8) | color.R;
            DwmSetWindowAttribute(hwnd, DwmwaCaptionColor, ref colorRef, sizeof(int));
        }
        catch (Exception ex)
        {
            AppLog.Append(NovaVoxPaths.BaseDirectory, $"[Fenêtre] Couleur de la barre de titre indisponible (Windows 11 requis) : {ex.Message}.", "diagnostic");
        }
    }
}
