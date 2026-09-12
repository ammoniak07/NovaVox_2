namespace NovaVox.Core.Tts;

/// <summary>
/// Détecte une demande d'interruption vocale ("Gemini, stop") — port de
/// Api._is_stop_phrase (app.py). Exige le nom de l'IA en plus du mot
/// d'arrêt pour éviter qu'un "stop" capté par hasard (vidéo, discussion)
/// ne coupe la voix à tort.
/// </summary>
public static class TtsStopPhrase
{
    private static readonly string[] StopKeywords =
        { "stop", "stoppe", "arrête", "arrete", "silence", "tais-toi", "tais toi", "chut" };

    public static bool IsStopPhrase(string textNorm, string geminiName)
    {
        var wakeWord = geminiName.ToLowerInvariant().Trim();
        if (wakeWord.Length == 0 || !textNorm.Contains(wakeWord, StringComparison.Ordinal)) return false;
        return StopKeywords.Any(k => textNorm.Contains(k, StringComparison.Ordinal));
    }
}
