using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Security.Cryptography;

namespace NovaVox.App.Targeting;

/// <summary>
/// Capture une zone de l'écran et en lit le texte avec l'OCR intégré à
/// Windows (Windows.Media.Ocr : hors ligne, gratuit, sans rien installer).
/// L'image est agrandie et mise en noir sur blanc avant lecture : le texte
/// holographique du HUD (clair sur fond sombre, petit) se lit bien mieux.
/// Le jeu doit être en « fenêtré sans bordure » : en plein écran exclusif,
/// Windows ne laisse pas capturer l'image du jeu.
/// </summary>
public sealed class HudTextReader : IDisposable
{
    private readonly OcrEngine? _engine;

    /// <summary>Langue utilisée par l'OCR (anglais de préférence : les noms de vaisseaux sont en anglais).</summary>
    public string? LanguageTag { get; }

    public bool IsAvailable => _engine is not null;

    public HudTextReader()
    {
        foreach (var tag in new[] { "en-US", "en-GB", "en" })
        {
            var language = OcrEngine.AvailableRecognizerLanguages.FirstOrDefault(l => l.LanguageTag.StartsWith(tag, StringComparison.OrdinalIgnoreCase));
            if (language is null) continue;
            _engine = OcrEngine.TryCreateFromLanguage(language);
            if (_engine is not null) break;
        }
        _engine ??= OcrEngine.TryCreateFromUserProfileLanguages();
        LanguageTag = _engine?.RecognizerLanguage?.LanguageTag;
    }

    /// <summary>Capture la zone (pixels réels de l'écran) et renvoie les lignes de texte lues.</summary>
    public async Task<IReadOnlyList<string>> ReadAsync(Rectangle zone, CancellationToken token = default)
    {
        if (_engine is null || zone.Width < 4 || zone.Height < 4) return Array.Empty<string>();
        using var prepared = CaptureAndPrepare(zone);
        token.ThrowIfCancellationRequested();
        using var bitmap = ToSoftwareBitmap(prepared);
        var result = await _engine.RecognizeAsync(bitmap);
        return result.Lines.Select(l => l.Text).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
    }

    /// <summary>Capture + agrandissement (petit texte) + niveaux de gris inversés et contrastés.</summary>
    private static Bitmap CaptureAndPrepare(Rectangle zone)
    {
        using var raw = new Bitmap(zone.Width, zone.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(raw))
            g.CopyFromScreen(zone.Left, zone.Top, 0, 0, zone.Size, CopyPixelOperation.SourceCopy);

        // Le texte du HUD fait souvent 10-14 px de haut : on l'agrandit pour l'OCR (sans dépasser sa limite).
        var scale = zone.Height < 120 ? 3 : zone.Height < 300 ? 2 : 1;
        var max = (int)OcrEngine.MaxImageDimension;
        while (scale > 1 && (zone.Width * scale > max || zone.Height * scale > max)) scale--;
        var scaled = new Bitmap(zone.Width * scale, zone.Height * scale, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(scaled))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(raw, 0, 0, scaled.Width, scaled.Height);
        }
        ToDarkTextOnLight(scaled);
        return scaled;
    }

    /// <summary>
    /// Niveaux de gris (canal le plus lumineux : le HUD est coloré), étirés sur
    /// toute la plage, puis inversés si le fond est sombre — l'OCR préfère du
    /// texte foncé sur fond clair.
    /// </summary>
    private static void ToDarkTextOnLight(Bitmap bitmap)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[data.Stride * data.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            int min = 255, maxV = 0;
            long sum = 0;
            var count = 0;
            for (var y = 0; y < data.Height; y++)
            {
                var row = y * data.Stride;
                for (var x = 0; x < data.Width; x++)
                {
                    var i = row + x * 4;
                    var v = Math.Max(bytes[i], Math.Max(bytes[i + 1], bytes[i + 2]));
                    bytes[i] = (byte)v;
                    min = Math.Min(min, v);
                    maxV = Math.Max(maxV, v);
                    sum += v;
                    count++;
                }
            }
            var invert = count > 0 && sum / count < 128; // fond sombre → texte clair à inverser
            var range = Math.Max(1, maxV - min);
            for (var y = 0; y < data.Height; y++)
            {
                var row = y * data.Stride;
                for (var x = 0; x < data.Width; x++)
                {
                    var i = row + x * 4;
                    var v = (bytes[i] - min) * 255 / range;
                    if (invert) v = 255 - v;
                    var b = (byte)Math.Clamp(v, 0, 255);
                    bytes[i] = b;
                    bytes[i + 1] = b;
                    bytes[i + 2] = b;
                    bytes[i + 3] = 255;
                }
            }
            Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static SoftwareBitmap ToSoftwareBitmap(Bitmap bitmap)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            // Format32bppArgb est stocké en BGRA en mémoire : le format attendu par l'OCR.
            var bytes = new byte[bitmap.Width * bitmap.Height * 4];
            for (var y = 0; y < bitmap.Height; y++)
                Marshal.Copy(data.Scan0 + y * data.Stride, bytes, y * bitmap.Width * 4, bitmap.Width * 4);
            var buffer = CryptographicBuffer.CreateFromByteArray(bytes);
            return SoftwareBitmap.CreateCopyFromBuffer(buffer, BitmapPixelFormat.Bgra8, bitmap.Width, bitmap.Height, BitmapAlphaMode.Premultiplied);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    public void Dispose()
    {
    }
}
