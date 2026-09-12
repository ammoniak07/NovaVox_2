using System.Text;
using System.Text.Json.Nodes;
using static NovaVox.Core.Json.JsonHelpers;

namespace NovaVox.Core.Gemini;

public sealed record GeminiMessage(string Role, string Content);

/// <summary>
/// Construction du corps JSON de la requête Gemini generateContent et
/// extraction de la réponse — logique pure extraite de
/// Api._gemini_reply_thread (app.py), pour rester testable sans appel
/// réseau réel.
/// </summary>
public static class GeminiRequestBuilder
{
    public static string RequestUrl(string model) => $"{GeminiModels.ApiBaseUrl}/models/{model}:generateContent";

    /// <summary>Gemini attend tout l'historique à chaque appel ; son rôle assistant s'appelle "model", pas "assistant".</summary>
    public static JsonObject BuildRequestBody(IReadOnlyList<GeminiMessage> history, string systemText, string responseLength)
    {
        var contents = new JsonArray();
        foreach (var m in history)
        {
            contents.Add(new JsonObject
            {
                ["role"] = m.Role == "assistant" ? "model" : "user",
                ["parts"] = new JsonArray(new JsonObject { ["text"] = m.Content }),
            });
        }

        return new JsonObject
        {
            ["contents"] = contents,
            ["systemInstruction"] = new JsonObject
            {
                ["parts"] = new JsonArray(new JsonObject { ["text"] = systemText }),
            },
            ["generationConfig"] = new JsonObject
            {
                ["maxOutputTokens"] = GeminiModels.MaxOutputTokensFor(responseLength),
                ["temperature"] = 0.7,
                ["thinkingConfig"] = new JsonObject { ["thinkingLevel"] = GeminiModels.ThinkingLevelFor(responseLength) },
            },
        };
    }

    /// <summary>Lit candidates[0].content.parts[*].text, concaténés — chaîne vide si absent/inattendu.</summary>
    public static string ExtractReplyText(JsonNode? responseBody)
    {
        if (responseBody?["candidates"] is not JsonArray { Count: > 0 } candidates) return "";
        if (candidates[0]?["content"]?["parts"] is not JsonArray parts) return "";

        var sb = new StringBuilder();
        foreach (var part in parts)
            sb.Append(GetString(part?["text"]));
        return sb.ToString().Trim();
    }

    /// <summary>Message d'erreur de l'API (error.message d'une réponse HTTP non-200), chaîne vide si absent.</summary>
    public static string ExtractErrorMessage(JsonNode? responseBody) =>
        GetString(responseBody?["error"]?["message"]);

    /// <summary>Conserve seulement les <see cref="GeminiModels.MaxHistoryMessages"/> derniers messages.</summary>
    public static List<GeminiMessage> TrimHistory(List<GeminiMessage> history) =>
        history.Count > GeminiModels.MaxHistoryMessages
            ? history.Skip(history.Count - GeminiModels.MaxHistoryMessages).ToList()
            : history;
}
