using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using static NovaVox.Core.Json.JsonHelpers;

namespace NovaVox.Core.Gemini;

/// <summary>
/// Recherche de contexte sur le wiki communautaire Star Citizen
/// (starcitizen.tools, non affilié à CIG) — logique pure extraite de
/// Api._gemini_wiki_reference_block/_wiki_extract_entity_en/
/// _wiki_search_page_title/_wiki_fetch_page_text/_wiki_html_to_text
/// (app.py). Les appels réseau eux-mêmes vivent dans NovaVox.App.Gemini.
/// GeminiClient, pour que ce module reste testable sans réseau.
/// </summary>
public static class StarCitizenWiki
{
    public const string ApiUrl = "https://starcitizen.tools/api.php";
    public const int ExtractMaxChars = 4000;

    public static string SearchUrl(string term) =>
        $"{ApiUrl}?action=query&list=search&srsearch={Uri.EscapeDataString(term)}&srlimit=1&format=json";

    public static string PageUrl(string title) =>
        $"{ApiUrl}?action=parse&page={Uri.EscapeDataString(title)}&format=json&prop=text%7Ccategories&redirects=1";

    /// <summary>
    /// Prompt envoyé à Gemini pour identifier, en anglais, l'entité précise
    /// visée par la question (nom de page probable sur le wiki anglais) —
    /// inclut les derniers échanges pour gérer les questions de suivi qui
    /// ne renomment pas l'entité (ex. « il a combien de HP de bouclier ? »
    /// après une question sur le Hull C).
    /// </summary>
    public static string BuildEntityExtractionPrompt(IReadOnlyList<GeminiMessage> recentHistory, string question)
    {
        var transcript = recentHistory.Count > 0
            ? string.Join("\n", recentHistory.Select(m => $"{(m.Role == "user" ? "User" : "Assistant")}: {m.Content}"))
            : $"User: {question}";

        return
            "You help find the right article on the English Star Citizen wiki " +
            "(starcitizen.tools) for a user's question, which may be written in " +
            "any language. Below is the end of a conversation with a Star " +
            "Citizen voice assistant (the last line is the current question) — " +
            "use the earlier lines only to resolve a question that refers back " +
            "to something already named, without repeating it (e.g. \"how much " +
            "shield HP does it have?\" right after a ship was named).\n\n" +
            $"{transcript}\n\n" +
            "Reply with ONLY the English name of the one specific Star Citizen " +
            "game entity (ship, ground vehicle, weapon, item, location, star " +
            "system, organization...) the LAST question is about, suitable as " +
            "a wiki search term — and matching the exact wiki page title. Some " +
            "names are ambiguous on this wiki (e.g. a planet sharing its name " +
            "with the manufacturer that operates it, like MicroTech): when that " +
            "is the case, disambiguate using the wiki's own convention, a short " +
            "type in parentheses after the name (e.g. \"MicroTech (planet)\"), " +
            "based on what the question is actually asking about. If it is not " +
            "about one specific named entity, reply with exactly: NONE";
    }

    /// <summary>Corps JSON du petit appel Gemini d'identification d'entité (10s max côté appelant, réponse très courte).</summary>
    public static JsonObject BuildEntityExtractionRequestBody(string prompt) => new()
    {
        ["contents"] = new JsonArray(new JsonObject
        {
            ["role"] = "user",
            ["parts"] = new JsonArray(new JsonObject { ["text"] = prompt }),
        }),
        ["generationConfig"] = new JsonObject
        {
            ["maxOutputTokens"] = 30,
            ["thinkingConfig"] = new JsonObject { ["thinkingLevel"] = "minimal" },
        },
    };

    /// <summary>Nom d'entité extrait de la réponse Gemini, ou null si absent/"NONE".</summary>
    public static string? ExtractEntityTerm(JsonNode? responseBody)
    {
        var term = GeminiRequestBuilder.ExtractReplyText(responseBody).Trim();
        return term.Length == 0 || term.Equals("NONE", StringComparison.OrdinalIgnoreCase) ? null : term;
    }

    /// <summary>Titre de la meilleure page trouvée par la recherche MediaWiki standard, ou null si aucun résultat.</summary>
    public static string? ExtractSearchTitle(JsonNode? responseBody) =>
        responseBody?["query"]?["search"] is JsonArray { Count: > 0 } results
            ? GetStringOrNull(results[0]?["title"])
            : null;

    /// <summary>Vrai si la page relève d'une catégorie de désambiguïsation (juste une liste de liens, pas de contenu exploitable).</summary>
    public static bool IsDisambiguationPage(JsonNode? responseBody)
    {
        if (responseBody?["parse"]?["categories"] is not JsonArray categories) return false;
        return categories.Any(c => GetString(c?["*"]).Contains("disambig", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Extrait de texte simple de la page (désambiguïsation ou HTML vide →
    /// null), tronqué à <see cref="ExtractMaxChars"/> pour ne pas peser sur
    /// le budget de tokens de la réponse Gemini.
    /// </summary>
    public static string? ExtractPageText(JsonNode? responseBody)
    {
        if (IsDisambiguationPage(responseBody)) return null;
        var rawHtml = GetString(responseBody?["parse"]?["text"]?["*"]);
        if (rawHtml.Length == 0) return null;
        var text = HtmlToText(rawHtml);
        return text.Length > ExtractMaxChars ? text[..ExtractMaxChars] : text;
    }

    /// <summary>
    /// Convertit le HTML brut d'une page du wiki en texte simple : retire
    /// scripts/styles puis toutes les balises, et normalise les espaces.
    /// Volontairement basique — suffisant pour donner un extrait lisible à
    /// Gemini, pas pour un rendu fidèle.
    /// </summary>
    public static string HtmlToText(string rawHtml)
    {
        var text = Regex.Replace(rawHtml, "(?is)<(script|style)[^>]*>.*?</\\1>", " ");
        text = Regex.Replace(text, "(?s)<[^>]+>", " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    /// <summary>Bloc de contexte (en anglais, à ajouter au prompt système) référençant l'extrait de la page trouvée.</summary>
    public static string BuildReferenceBlock(string title, string extract) =>
        "\n\nAdditional reference (Star Citizen community wiki, in English, page " +
        $"\"{title}\"). Use this information if it helps answer the user's " +
        "question, but always reply in the language specified above — never " +
        "switch to English just because this reference is in English. " +
        "IMPORTANT: for any specific figure about this topic (stats, counts, " +
        "capacities, prices...), state ONLY what this reference actually says " +
        "— never add another specific figure from memory, even one you believe " +
        "you know, since this is a live-service game whose stats change with " +
        "balance patches and your training data can be outdated. If the user " +
        "asks about a specific detail that isn't in this reference, say " +
        "plainly that you don't have that exact figure rather than guessing " +
        $"one:\n{extract}";
}
