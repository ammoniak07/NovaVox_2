using System.Text;

namespace NovaVox.Core.GameLog;

public static class GameLogText
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Corrige un bug d'encodage vérifié dans le Game.log : les caractères
    /// accentués français sont doublement mal interprétés (UTF-8 relu
    /// comme Latin-1), ex. "terminÃ©" au lieu de "terminé". Port de
    /// _fix_mojibake (game_log_watcher.py) : ré-encode en Latin-1 puis
    /// redécode en UTF-8, silencieusement inchangé si l'opération échoue.
    /// </summary>
    public static string FixMojibake(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        // Python's str.encode("latin1") lève une exception pour tout
        // caractère hors de la plage Latin-1 (0-255) : dans ce cas le
        // texte est retourné inchangé, jamais partiellement corrigé.
        foreach (var c in text)
        {
            if (c > 0xFF) return text;
        }
        var bytes = new byte[text.Length];
        for (int i = 0; i < text.Length; i++) bytes[i] = (byte)text[i];
        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return text;
        }
    }
}
