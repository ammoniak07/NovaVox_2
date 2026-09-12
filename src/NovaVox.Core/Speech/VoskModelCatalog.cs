namespace NovaVox.Core.Speech;

/// <summary>Un modèle Vosk téléchargeable proposé dans l'interface — port de VOSK_MODELS (app.py).</summary>
public sealed record VoskModelInfo(string Id, string Lang, string Label, string Description, string Url);

/// <summary>
/// Port de VOSK_MODELS / VOSK_MODEL_IDS_BY_LANG / vosk_models_for_language
/// (app.py). URLs vérifiées sur https://alphacephei.com/vosk/models.
/// </summary>
public static class VoskModelCatalog
{
    public static readonly IReadOnlyList<VoskModelInfo> Models = new List<VoskModelInfo>
    {
        new("vosk-model-small-fr-0.22", "fr", "Français — rapide (petit modèle)",
            "~41 Mo, chargement quasi instantané, reconnaissance correcte.",
            "https://alphacephei.com/vosk/models/vosk-model-small-fr-0.22.zip"),
        new("vosk-model-fr-0.22", "fr", "Français — précis (grand modèle)",
            "~1,4 Go, plus long à télécharger et charger, mais reconnaissance plus fine.",
            "https://alphacephei.com/vosk/models/vosk-model-fr-0.22.zip"),
        new("vosk-model-small-en-us-0.15", "en", "English — fast (small model)",
            "~40 MB, near-instant loading, decent recognition.",
            "https://alphacephei.com/vosk/models/vosk-model-small-en-us-0.15.zip"),
        new("vosk-model-en-us-0.22", "en", "English — precise (large model)",
            "~1.8 GB, longer to download and load, but finer recognition.",
            "https://alphacephei.com/vosk/models/vosk-model-en-us-0.22.zip"),
        new("vosk-model-small-nl-0.22", "nl", "Nederlands — snel (klein model)",
            "~39 MB, vrijwel direct geladen, degelijke herkenning.",
            "https://alphacephei.com/vosk/models/vosk-model-small-nl-0.22.zip"),
        new("vosk-model-nl-spraakherkenning-0.6", "nl", "Nederlands — precies (groot model)",
            "~860 MB, langer downloaden en laden, maar fijnere herkenning.",
            "https://alphacephei.com/vosk/models/vosk-model-nl-spraakherkenning-0.6.zip"),
        new("vosk-model-small-es-0.42", "es", "Español — rápido (modelo pequeño)",
            "~39 MB, carga casi instantánea, reconocimiento correcto.",
            "https://alphacephei.com/vosk/models/vosk-model-small-es-0.42.zip"),
        new("vosk-model-es-0.42", "es", "Español — preciso (modelo grande)",
            "~1,4 GB, descarga y carga más largas, pero reconocimiento más fino.",
            "https://alphacephei.com/vosk/models/vosk-model-es-0.42.zip"),
        new("vosk-model-small-it-0.22", "it", "Italiano — veloce (modello piccolo)",
            "~48 MB, caricamento quasi istantaneo, riconoscimento corretto.",
            "https://alphacephei.com/vosk/models/vosk-model-small-it-0.22.zip"),
        new("vosk-model-it-0.22", "it", "Italiano — preciso (modello grande)",
            "~1,2 GB, download e caricamento più lunghi, ma riconoscimento più fine.",
            "https://alphacephei.com/vosk/models/vosk-model-it-0.22.zip"),
        new("vosk-model-small-de-0.15", "de", "Deutsch — schnell (kleines Modell)",
            "~45 MB, quasi sofortiges Laden, ordentliche Erkennung.",
            "https://alphacephei.com/vosk/models/vosk-model-small-de-0.15.zip"),
        new("vosk-model-de-0.21", "de", "Deutsch — präzise (großes Modell)",
            "~1,9 GB, längerer Download und Ladezeit, aber feinere Erkennung.",
            "https://alphacephei.com/vosk/models/vosk-model-de-0.21.zip"),
    };

    private static readonly IReadOnlyDictionary<string, string[]> IdsByLang = new Dictionary<string, string[]>
    {
        ["fr"] = new[] { "vosk-model-small-fr-0.22", "vosk-model-fr-0.22" },
        ["en"] = new[] { "vosk-model-small-en-us-0.15", "vosk-model-en-us-0.22" },
        ["nl"] = new[] { "vosk-model-small-nl-0.22", "vosk-model-nl-spraakherkenning-0.6" },
        ["es"] = new[] { "vosk-model-small-es-0.42", "vosk-model-es-0.42" },
        ["it"] = new[] { "vosk-model-small-it-0.22", "vosk-model-it-0.22" },
        ["de"] = new[] { "vosk-model-small-de-0.15", "vosk-model-de-0.21" },
    };

    public const string DefaultLanguage = "fr";

    public static IReadOnlyList<VoskModelInfo> ModelsForLanguage(string? lang)
    {
        var ids = IdsByLang.TryGetValue(lang ?? "", out var found) ? found : IdsByLang[DefaultLanguage];
        var byId = Models.ToDictionary(m => m.Id);
        return ids.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
    }
}
