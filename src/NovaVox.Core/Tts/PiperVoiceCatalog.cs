namespace NovaVox.Core.Tts;

/// <summary>Une voix Piper curatée téléchargeable — port de PIPER_VOICES (app.py).</summary>
public sealed record PiperVoiceInfo(string Id, string Lang, string Label, string UrlBase);

/// <summary>
/// Port de PIPER_VOICES (app.py) : un éventail de voix curatées par langue
/// parmi celles publiées sur le dépôt Hugging Face officiel de Piper
/// (rhasspy/piper-voices). "lang" sert seulement à mettre en avant les voix
/// de la langue de l'interface, pas à restreindre le choix.
/// </summary>
public static class PiperVoiceCatalog
{
    private const string BaseUrl = "https://huggingface.co/rhasspy/piper-voices/resolve/main";

    public static readonly IReadOnlyList<PiperVoiceInfo> Voices = new List<PiperVoiceInfo>
    {
        new("fr_FR-siwis-medium", "fr", "Siwis — voix féminine, très naturelle (qualité medium)", $"{BaseUrl}/fr/fr_FR/siwis/medium/fr_FR-siwis-medium"),
        new("fr_FR-siwis-low", "fr", "Siwis — même voix féminine, version légère/rapide", $"{BaseUrl}/fr/fr_FR/siwis/low/fr_FR-siwis-low"),
        new("fr_FR-tom-medium", "fr", "Tom — voix masculine, ton neutre", $"{BaseUrl}/fr/fr_FR/tom/medium/fr_FR-tom-medium"),
        new("fr_FR-gilles-low", "fr", "Gilles — voix masculine", $"{BaseUrl}/fr/fr_FR/gilles/low/fr_FR-gilles-low"),
        new("fr_FR-upmc-medium", "fr", "UPMC — voix mixte (jessica/pierre)", $"{BaseUrl}/fr/fr_FR/upmc/medium/fr_FR-upmc-medium"),
        new("fr_FR-mls_1840-low", "fr", "MLS 1840 — voix alternative", $"{BaseUrl}/fr/fr_FR/mls_1840/low/fr_FR-mls_1840-low"),
        new("en_US-amy-medium", "en", "Amy — female voice, conversational (medium quality)", $"{BaseUrl}/en/en_US/amy/medium/en_US-amy-medium"),
        new("en_US-ryan-medium", "en", "Ryan — male voice", $"{BaseUrl}/en/en_US/ryan/medium/en_US-ryan-medium"),
        new("en_US-lessac-medium", "en", "Lessac — neutral, very natural voice", $"{BaseUrl}/en/en_US/lessac/medium/en_US-lessac-medium"),
        new("nl_NL-pim-medium", "nl", "Pim — mannelijke stem", $"{BaseUrl}/nl/nl_NL/pim/medium/nl_NL-pim-medium"),
        new("nl_NL-ronnie-medium", "nl", "Ronnie — alternatieve stem", $"{BaseUrl}/nl/nl_NL/ronnie/medium/nl_NL-ronnie-medium"),
        new("nl_NL-mls-medium", "nl", "MLS — alternatieve stem", $"{BaseUrl}/nl/nl_NL/mls/medium/nl_NL-mls-medium"),
        new("es_ES-davefx-medium", "es", "Davefx — voz masculina", $"{BaseUrl}/es/es_ES/davefx/medium/es_ES-davefx-medium"),
        new("es_ES-sharvard-medium", "es", "Sharvard — voz alternativa", $"{BaseUrl}/es/es_ES/sharvard/medium/es_ES-sharvard-medium"),
        new("es_ES-mls_10246-low", "es", "MLS 10246 — voz alternativa, versión ligera/rápida", $"{BaseUrl}/es/es_ES/mls_10246/low/es_ES-mls_10246-low"),
        new("it_IT-paola-medium", "it", "Paola — voce femminile", $"{BaseUrl}/it/it_IT/paola/medium/it_IT-paola-medium"),
        new("it_IT-riccardo-x_low", "it", "Riccardo — voce maschile, versione molto leggera", $"{BaseUrl}/it/it_IT/riccardo/x_low/it_IT-riccardo-x_low"),
        new("de_DE-thorsten-medium", "de", "Thorsten — männliche Stimme, sehr natürlich", $"{BaseUrl}/de/de_DE/thorsten/medium/de_DE-thorsten-medium"),
        new("de_DE-kerstin-low", "de", "Kerstin — weibliche Stimme, leichte/schnelle Version", $"{BaseUrl}/de/de_DE/kerstin/low/de_DE-kerstin-low"),
        new("de_DE-mls-medium", "de", "MLS — alternative Stimme", $"{BaseUrl}/de/de_DE/mls/medium/de_DE-mls-medium"),
    };

    public static PiperVoiceInfo? Find(string? id) => Voices.FirstOrDefault(v => v.Id == id);

    /// <summary>
    /// Voix proposées pour une langue d'interface donnée (repli sur le
    /// français si la langue est inconnue ou n'a aucune voix curatée) —
    /// même principe que VoskModelCatalog.ModelsForLanguage, à la demande
    /// explicite de ne montrer/proposer au téléchargement que les voix de
    /// la langue actuellement sélectionnée plutôt que la liste complète.
    /// </summary>
    public static IReadOnlyList<PiperVoiceInfo> VoicesForLanguage(string? lang)
    {
        var matches = Voices.Where(v => v.Lang == lang).ToList();
        return matches.Count > 0 ? matches : Voices.Where(v => v.Lang == "fr").ToList();
    }
}
