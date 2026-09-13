namespace NovaVox.Core.Gemini;

public sealed record GeminiModelInfo(string Id, string Label, string Description)
{
    // Le ComboBox custom-templaté des réglages (MainWindow.xaml, voir
    // GeminiModelCombo) affiche l'élément sélectionné via SelectionBoxItem,
    // qui retombe sur ToString() plutôt que DisplayMemberPath dans ce cas
    // précis — sans ceci, la case affichait le ToString() généré par le
    // record ("GeminiModelInfo { Id = ..., Label = ..., ... }") au lieu du
    // libellé du modèle (même bug que ProfileInfo.ToString(), voir CommandStore.cs).
    public override string ToString() => Label;
}

/// <summary>
/// Modèles Gemini proposés, leurs limites gratuites quotidiennes (RPD) et
/// les réglages de génération par longueur de réponse — port des
/// constantes GEMINI_AVAILABLE_MODELS/GEMINI_DAILY_LIMITS/
/// AI_NUM_PREDICT_BY_LENGTH/GEMINI_THINKING_LEVEL_BY_LENGTH (app.py).
/// Google ajuste régulièrement ces modèles/quotas : voir les mêmes
/// commentaires côté Python si un modèle listé ici renvoie une 404.
/// </summary>
public static class GeminiModels
{
    public const string ApiBaseUrl = "https://generativelanguage.googleapis.com/v1beta";
    public const int MaxHistoryMessages = 20;
    public const double QuestionTimeoutSeconds = 8.0;

    public static readonly IReadOnlyList<GeminiModelInfo> AvailableModels = new List<GeminiModelInfo>
    {
        new("gemini-3.6-flash", "Gemini 3.6 Flash — recommandé",
            "Rapide, gratuit avec un quota généreux pour un usage personnel, bon compromis qualité/vitesse."),
        // "gemini-3.6-flash-lite" retiré : n'existe pas côté API (404 sur
        // v1beta generateContent, confirmé par un utilisateur) — n'était
        // qu'une supposition par analogie avec gemini-3.5-flash-lite,
        // jamais un modèle réellement publié par Google.
        new("gemini-3.5-flash-lite", "Gemini 3.5 Flash-Lite — le plus rapide",
            "Quota gratuit plus élevé et réponses plus rapides, un peu moins riches que Flash."),
    };

    public static readonly IReadOnlyDictionary<string, int> DailyLimits = new Dictionary<string, int>
    {
        ["gemini-3.6-flash"] = 500,
        ["gemini-3.5-flash-lite"] = 1500,
    };

    public const int DailyLimitDefault = 500;

    public static readonly IReadOnlyDictionary<string, int> MaxOutputTokensByLength = new Dictionary<string, int>
    {
        ["short"] = 512,
        ["normal"] = 1024,
        ["long"] = 2048,
    };

    public static readonly IReadOnlyDictionary<string, string> ThinkingLevelByLength = new Dictionary<string, string>
    {
        ["short"] = "minimal",
        ["normal"] = "low",
        ["long"] = "medium",
    };

    public static int DailyLimitFor(string model) => DailyLimits.TryGetValue(model, out var limit) ? limit : DailyLimitDefault;

    public static int MaxOutputTokensFor(string responseLength) =>
        MaxOutputTokensByLength.TryGetValue(responseLength, out var v) ? v : MaxOutputTokensByLength["normal"];

    public static string ThinkingLevelFor(string responseLength) =>
        ThinkingLevelByLength.TryGetValue(responseLength, out var v) ? v : ThinkingLevelByLength["normal"];
}
