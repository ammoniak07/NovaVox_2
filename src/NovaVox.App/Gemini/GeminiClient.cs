using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using NovaVox.Core.Config;
using NovaVox.Core.Gemini;
using NovaVox.Core.GameLog;

namespace NovaVox.App.Gemini;

public sealed class GeminiReplyEventArgs : EventArgs
{
    public required string Reply { get; init; }
    public bool IsError { get; init; }
}

/// <summary>
/// Client Gemini (assistant IA embarqué) — port de Api._gemini_ask /
/// gemini_ask_text / _gemini_reply_thread (app.py). La recherche de
/// contexte sur le wiki communautaire Star Citizen
/// (_gemini_wiki_reference_block) n'est PAS encore portée : c'est une
/// amélioration "best-effort" annexe côté Python (jamais bloquante),
/// laissée en TODO pour une passe ultérieure plutôt que de retarder le
/// flux de conversation principal.
/// </summary>
public sealed class GeminiClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(65) };

    private readonly List<GeminiMessage> _history = new();

    public AiConfig Config { get; }
    public AiConfigStore? Store { get; set; }
    public Func<GameLogState?>? GameLogStateProvider { get; set; }

    public event EventHandler<string>? UserMessageAdded;
    public event EventHandler<GeminiReplyEventArgs>? ReplyReceived;

    public GeminiClient(AiConfig config, AiConfigStore? store = null)
    {
        Config = config;
        Store = store;
    }

    /// <summary>Question posée à voix haute (mot d'activation détecté par VoiceCommandDispatcher).</summary>
    public Task AskAsync(string question) => AskInternalAsync(question, forceVoiceOutput: null);

    /// <summary>Question TAPÉE dans un éventuel panneau de discussion — `speak` ne contrôle que cette réponse précise.</summary>
    public Task AskTextAsync(string question, bool speak = true) => AskInternalAsync(question, forceVoiceOutput: speak);

    private async Task AskInternalAsync(string question, bool? forceVoiceOutput)
    {
        question = question.Trim();
        if (question.Length == 0) return;

        _history.Add(new GeminiMessage("user", question));
        UserMessageAdded?.Invoke(this, question);

        var reply = await BuildReplyAsync(question).ConfigureAwait(false);

        _history.Add(new GeminiMessage("assistant", reply.Text));
        var trimmed = GeminiRequestBuilder.TrimHistory(_history);
        if (!ReferenceEquals(trimmed, _history))
        {
            _history.Clear();
            _history.AddRange(trimmed);
        }

        ReplyReceived?.Invoke(this, new GeminiReplyEventArgs { Reply = reply.Text, IsError = reply.IsError });
        // ReplyReceived : à l'appelant de décider de lire la réponse à voix
        // haute (should_speak = forceVoiceOutput ?? Config.GeminiVoiceOutput
        // côté Python — ce dernier réglage n'existe pas encore dans
        // AiConfig, voir TODO plus bas) sans jamais lire un message d'erreur.
    }

    private async Task<(string Text, bool IsError)> BuildReplyAsync(string question)
    {
        var apiKey = Config.GeminiApiKey.Trim();
        if (apiKey.Length == 0)
        {
            return ("[Erreur] Aucune clé API Gemini configurée. Ouvre Réglages > 🌟 IA Gemini " +
                    "et renseigne ta clé (gratuite sur aistudio.google.com).", true);
        }

        var (used, limit) = GeminiQuota.SyncState(Config);
        Store?.Save(Config); // persiste une éventuelle remise à zéro quotidienne du compteur
        if (used >= limit)
        {
            var modelLabel = GeminiModels.AvailableModels.FirstOrDefault(m => m.Id == Config.GeminiModel)?.Label ?? Config.GeminiModel;
            var switchHint = Config.GeminiModel != "gemini-3.5-flash-lite"
                ? " ou passe sur Gemini Flash-Lite dans les réglages (limite quotidienne plus haute)"
                : "";
            return ($"[Limite atteinte] Tu as utilisé les {limit} requêtes gratuites du jour pour " +
                    $"{modelLabel}. Le quota se réinitialise à minuit, heure du Pacifique (Californie)" +
                    $"{switchHint}.", true);
        }

        var gameStateBlock = GameLogStateProvider is not null ? GameStatePrompt.ToPromptBlock(GameLogStateProvider()) : "";

        var systemText = GeminiPrompt.BuildSystemPrompt(
            Config.GeminiName, Config.GeminiCustomContext, Config.UserName, Config.GeminiResponseLength, Config.UiLanguage)
            + gameStateBlock;

        var requestBody = GeminiRequestBuilder.BuildRequestBody(_history, systemText, Config.GeminiResponseLength);
        var url = GeminiRequestBuilder.RequestUrl(Config.GeminiModel);

        GeminiQuota.RecordRequest(Config);
        Store?.Save(Config);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("x-goog-api-key", apiKey);

            using var response = await Http.SendAsync(request).ConfigureAwait(false);
            var bodyText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var body = JsonNode.Parse(bodyText);

            if (!response.IsSuccessStatusCode)
            {
                var msg = GeminiRequestBuilder.ExtractErrorMessage(body);
                if (msg.Length == 0) msg = response.ReasonPhrase ?? response.StatusCode.ToString();
                return ($"[Erreur] Gemini a renvoyé une erreur ({(int)response.StatusCode}) : {msg}", true);
            }

            var text = GeminiRequestBuilder.ExtractReplyText(body);
            return (text.Length == 0 ? "(réponse vide du modèle)" : text, false);
        }
        catch (TaskCanceledException)
        {
            return ("[Erreur] Gemini n'a pas répondu en moins de 60 secondes. Réessaie.", true);
        }
        catch (HttpRequestException e)
        {
            return ($"[Erreur] Impossible de contacter Gemini ({e.Message}). Vérifie ta connexion internet.", true);
        }
        catch (Exception e)
        {
            return ($"[Erreur] {e.Message}", true);
        }
    }
}
