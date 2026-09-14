namespace NovaVox.Core.Localization;

/// <summary>
/// Traductions de l'interface .NET/WPF — port partiel de gui/i18n.js (app.py
/// Python), mêmes langues (fr/en/nl/es/it/de) et mêmes clés là où un
/// contrôle WPF équivalent existe. Couverture actuelle : barre du haut,
/// indicateur d'écoute (statut + bouton Engager/Couper), titre et onglets
/// des Réglages, langue de l'interface, modèle vocal (Vosk), affichage du
/// journal système, fenêtre de sélection de touche. Comme côté Python, le
/// reste de l'interface (Gemini/Game.log détaillés, listes de commandes,
/// messages du journal) reste en français pour l'instant — étendre Strings
/// ci-dessous progressivement, sans casser ce qui existe déjà (une clé
/// absente retombe silencieusement sur le français).
/// </summary>
public static class UiLocalization
{
    public const string DefaultLanguage = "fr";

    public static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Strings =
        new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["fr"] = new Dictionary<string, string>
            {
                ["topbar.subtitle"] = "Reconnaissance vocale hors-ligne — Star Citizen",
                ["topbar.gemini"] = "🌟 Assistant Gemini",
                ["topbar.gamelog"] = "🛰 Game.log",
                ["topbar.settings"] = "Réglages",
                ["topbar.theme"] = "Thème de couleurs",
                ["status.idle.label"] = "Arrêté",
                ["status.idle.sub"] = "Système en veille",
                ["status.loading.label"] = "Chargement...",
                ["status.loading.sub"] = "Initialisation du modèle",
                ["status.active.label"] = "Écoute active",
                ["status.active.sub"] = "Écoute en cours",
                ["engage.start"] = "▶ Engager l'écoute",
                ["engage.stop"] = "■ Couper l'écoute",
                ["settings.title"] = "⚙️ Réglages",
                ["settings.tab.sons"] = "🔊 Sons",
                ["settings.tab.gemini"] = "🌟 IA Gemini",
                ["settings.tab.gamelog"] = "🛰 Game.log",
                ["settings.language.label"] = "Langue de l'interface",
                ["settings.model.label"] = "Modèle vocal (Vosk)",
                ["settings.showlog"] = "Afficher le journal système",
                ["kb.cancel"] = "Annuler",
                ["kb.confirm"] = "Valider",
            },
            ["en"] = new Dictionary<string, string>
            {
                ["topbar.subtitle"] = "Offline speech recognition — Star Citizen",
                ["topbar.gemini"] = "🌟 Gemini Assistant",
                ["topbar.gamelog"] = "🛰 Game.log",
                ["topbar.settings"] = "Settings",
                ["topbar.theme"] = "Color theme",
                ["status.idle.label"] = "Stopped",
                ["status.idle.sub"] = "System idle",
                ["status.loading.label"] = "Loading...",
                ["status.loading.sub"] = "Initializing model",
                ["status.active.label"] = "Listening",
                ["status.active.sub"] = "Listening in progress",
                ["engage.start"] = "▶ Start listening",
                ["engage.stop"] = "■ Stop listening",
                ["settings.title"] = "⚙️ Settings",
                ["settings.tab.sons"] = "🔊 Sound",
                ["settings.tab.gemini"] = "🌟 Gemini AI",
                ["settings.tab.gamelog"] = "🛰 Game.log",
                ["settings.language.label"] = "Interface language",
                ["settings.model.label"] = "Voice model (Vosk)",
                ["settings.showlog"] = "Show system log",
                ["kb.cancel"] = "Cancel",
                ["kb.confirm"] = "Confirm",
            },
            ["nl"] = new Dictionary<string, string>
            {
                ["topbar.subtitle"] = "Offline spraakherkenning — Star Citizen",
                ["topbar.gemini"] = "🌟 Gemini-assistent",
                ["topbar.gamelog"] = "🛰 Game.log",
                ["topbar.settings"] = "Instellingen",
                ["topbar.theme"] = "Kleurthema",
                ["status.idle.label"] = "Gestopt",
                ["status.idle.sub"] = "Systeem in rust",
                ["status.loading.label"] = "Laden...",
                ["status.loading.sub"] = "Model initialiseren",
                ["status.active.label"] = "Luisteren",
                ["status.active.sub"] = "Luisteren actief",
                ["engage.start"] = "▶ Luisteren starten",
                ["engage.stop"] = "■ Luisteren stoppen",
                ["settings.title"] = "⚙️ Instellingen",
                ["settings.tab.sons"] = "🔊 Geluid",
                ["settings.tab.gemini"] = "🌟 Gemini-AI",
                ["settings.tab.gamelog"] = "🛰 Game.log",
                ["settings.language.label"] = "Taal van de interface",
                ["settings.model.label"] = "Spraakmodel (Vosk)",
                ["settings.showlog"] = "Systeemlogboek weergeven",
                ["kb.cancel"] = "Annuleren",
                ["kb.confirm"] = "Bevestigen",
            },
            ["es"] = new Dictionary<string, string>
            {
                ["topbar.subtitle"] = "Reconocimiento de voz sin conexión — Star Citizen",
                ["topbar.gemini"] = "🌟 Asistente Gemini",
                ["topbar.gamelog"] = "🛰 Game.log",
                ["topbar.settings"] = "Ajustes",
                ["topbar.theme"] = "Tema de colores",
                ["status.idle.label"] = "Detenido",
                ["status.idle.sub"] = "Sistema en espera",
                ["status.loading.label"] = "Cargando...",
                ["status.loading.sub"] = "Inicializando el modelo",
                ["status.active.label"] = "Escuchando",
                ["status.active.sub"] = "Escucha en curso",
                ["engage.start"] = "▶ Iniciar escucha",
                ["engage.stop"] = "■ Detener escucha",
                ["settings.title"] = "⚙️ Ajustes",
                ["settings.tab.sons"] = "🔊 Sonido",
                ["settings.tab.gemini"] = "🌟 IA Gemini",
                ["settings.tab.gamelog"] = "🛰 Game.log",
                ["settings.language.label"] = "Idioma de la interfaz",
                ["settings.model.label"] = "Modelo de voz (Vosk)",
                ["settings.showlog"] = "Mostrar el registro del sistema",
                ["kb.cancel"] = "Cancelar",
                ["kb.confirm"] = "Confirmar",
            },
            ["it"] = new Dictionary<string, string>
            {
                ["topbar.subtitle"] = "Riconoscimento vocale offline — Star Citizen",
                ["topbar.gemini"] = "🌟 Assistente Gemini",
                ["topbar.gamelog"] = "🛰 Game.log",
                ["topbar.settings"] = "Impostazioni",
                ["topbar.theme"] = "Tema colori",
                ["status.idle.label"] = "Fermato",
                ["status.idle.sub"] = "Sistema in attesa",
                ["status.loading.label"] = "Caricamento...",
                ["status.loading.sub"] = "Inizializzazione del modello",
                ["status.active.label"] = "In ascolto",
                ["status.active.sub"] = "Ascolto in corso",
                ["engage.start"] = "▶ Avvia ascolto",
                ["engage.stop"] = "■ Interrompi ascolto",
                ["settings.title"] = "⚙️ Impostazioni",
                ["settings.tab.sons"] = "🔊 Audio",
                ["settings.tab.gemini"] = "🌟 IA Gemini",
                ["settings.tab.gamelog"] = "🛰 Game.log",
                ["settings.language.label"] = "Lingua dell'interfaccia",
                ["settings.model.label"] = "Modello vocale (Vosk)",
                ["settings.showlog"] = "Mostra il registro di sistema",
                ["kb.cancel"] = "Annulla",
                ["kb.confirm"] = "Conferma",
            },
            ["de"] = new Dictionary<string, string>
            {
                ["topbar.subtitle"] = "Offline-Spracherkennung — Star Citizen",
                ["topbar.gemini"] = "🌟 Gemini-Assistent",
                ["topbar.gamelog"] = "🛰 Game.log",
                ["topbar.settings"] = "Einstellungen",
                ["topbar.theme"] = "Farbthema",
                ["status.idle.label"] = "Gestoppt",
                ["status.idle.sub"] = "System im Ruhezustand",
                ["status.loading.label"] = "Wird geladen...",
                ["status.loading.sub"] = "Modell wird initialisiert",
                ["status.active.label"] = "Zuhören",
                ["status.active.sub"] = "Zuhören läuft",
                ["engage.start"] = "▶ Zuhören starten",
                ["engage.stop"] = "■ Zuhören stoppen",
                ["settings.title"] = "⚙️ Einstellungen",
                ["settings.tab.sons"] = "🔊 Ton",
                ["settings.tab.gemini"] = "🌟 Gemini-KI",
                ["settings.tab.gamelog"] = "🛰 Game.log",
                ["settings.language.label"] = "Sprache der Oberfläche",
                ["settings.model.label"] = "Sprachmodell (Vosk)",
                ["settings.showlog"] = "Systemprotokoll anzeigen",
                ["kb.cancel"] = "Abbrechen",
                ["kb.confirm"] = "Bestätigen",
            },
        };

    /// <summary>Traduit une clé dans la langue donnée, avec repli sur le français puis sur la clé elle-même.</summary>
    public static string T(string? lang, string key)
    {
        if (Strings.TryGetValue(lang ?? "", out var dict) && dict.TryGetValue(key, out var value))
            return value;
        return Strings[DefaultLanguage].TryGetValue(key, out var fallback) ? fallback : key;
    }
}
