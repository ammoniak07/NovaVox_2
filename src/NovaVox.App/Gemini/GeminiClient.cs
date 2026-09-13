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
/// gemini_ask_text / _gemini_reply_thread (app.py), y compris la
/// recherche best-effort de contexte sur le wiki communautaire Star
/// Citizen (_gemini_wiki_reference_block/_wiki_extract_entity_en/
/// _wiki_search_page_title/_wiki_fetch_page_text) — voir
/// NovaVox.Core.Gemini.StarCitizenWiki pour la logique pure (JSON,
/// construction d'URL, HTML→texte) et BuildWikiReferenceBlockAsync
/// ci-dessous pour les appels réseau.
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

    /// <summary>Traces "[Info] Wiki SC : ..." destinées au journal système de l'interface (voir _log côté Python).</summary>
    public event EventHandler<(string Message, string Kind)>? Log;

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
            var maxLimit = GeminiModels.AvailableModels.Max(m => GeminiModels.DailyLimitFor(m.Id));
            var switchHint = limit < maxLimit
                ? " ou passe sur un modèle à quota plus élevé dans les réglages"
                : "";
            return ($"[Limite atteinte] Tu as utilisé les {limit} requêtes gratuites du jour pour " +
                    $"{modelLabel}. Le quota se réinitialise à minuit, heure du Pacifique (Californie)" +
                    $"{switchHint}.", true);
        }

        var gameStateBlock = GameLogStateProvider is not null ? GameStatePrompt.ToPromptBlock(GameLogStateProvider()) : "";
        var wikiBlock = Config.GeminiWikiEnabled && question.Length > 0
            ? await BuildWikiReferenceBlockAsync(question, apiKey, used, limit).ConfigureAwait(false)
            : "";

        var systemText = GeminiPrompt.BuildSystemPrompt(
            Config.GeminiName, Config.GeminiCustomContext, Config.UserName, Config.GeminiResponseLength, Config.UiLanguage)
            + gameStateBlock + wikiBlock;

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

    /// <summary>
    /// Best-effort : si la question semble viser une entité précise du jeu
    /// (vaisseau, objet, lieu...), cherche l'article correspondant sur le
    /// wiki communautaire Star Citizen et renvoie un bloc de contexte à
    /// ajouter au prompt système. Ne consomme le quota gratuit que s'il
    /// reste au moins 2 requêtes (1 pour cette recherche + 1 pour la
    /// vraie réponse) — et à la moindre erreur réseau/timeout, renvoie une
    /// chaîne vide sans jamais empêcher la réponse normale.
    /// </summary>
    private async Task<string> BuildWikiReferenceBlockAsync(string question, string apiKey, int quotaUsed, int quotaLimit)
    {
        if (quotaUsed + 2 > quotaLimit)
        {
            RaiseLog("[Info] Wiki SC : recherche sautée (quota gratuit du jour presque épuisé).", "info");
            return "";
        }

        // Nom de l'étape en cours, pour que le message d'erreur ci-dessous
        // précise LAQUELLE des 3 requêtes réseau a échoué plutôt qu'un
        // simple "ignorée (...)" sans indiquer où.
        var step = "identification de l'entité (appel Gemini)";
        string term;
        string title;
        string extract;
        try
        {
            var recentHistory = _history.Skip(Math.Max(0, _history.Count - 6)).ToList();
            var prompt = StarCitizenWiki.BuildEntityExtractionPrompt(recentHistory, question);
            var entityRequestBody = StarCitizenWiki.BuildEntityExtractionRequestBody(prompt);
            var entityUrl = GeminiRequestBuilder.RequestUrl(Config.GeminiModel);

            using var entityCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var entityRequest = new HttpRequestMessage(HttpMethod.Post, entityUrl)
            {
                Content = new StringContent(entityRequestBody.ToJsonString(), Encoding.UTF8, "application/json"),
            };
            entityRequest.Headers.Add("x-goog-api-key", apiKey);
            using var entityResponse = await Http.SendAsync(entityRequest, entityCts.Token).ConfigureAwait(false);
            entityResponse.EnsureSuccessStatusCode();
            var entityBodyText = await entityResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
            GeminiQuota.RecordRequest(Config);
            Store?.Save(Config);

            var entityTerm = StarCitizenWiki.ExtractEntityTerm(JsonNode.Parse(entityBodyText));
            if (entityTerm is null)
            {
                RaiseLog("[Info] Wiki SC : aucune entité précise identifiée dans la question.", "info");
                return "";
            }
            term = entityTerm;

            step = "recherche de la page (wiki)";
            using var searchCts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            using var searchRequest = new HttpRequestMessage(HttpMethod.Get, StarCitizenWiki.SearchUrl(term));
            searchRequest.Headers.UserAgent.ParseAdd("Mozilla/5.0");
            using var searchResponse = await Http.SendAsync(searchRequest, searchCts.Token).ConfigureAwait(false);
            searchResponse.EnsureSuccessStatusCode();
            var searchBodyText = await searchResponse.Content.ReadAsStringAsync().ConfigureAwait(false);

            var foundTitle = StarCitizenWiki.ExtractSearchTitle(JsonNode.Parse(searchBodyText));
            if (foundTitle is null)
            {
                RaiseLog($"[Info] Wiki SC : aucune page trouvée pour « {term} ».", "info");
                return "";
            }
            title = foundTitle;

            step = "récupération du contenu de la page (wiki)";
            using var pageCts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            using var pageRequest = new HttpRequestMessage(HttpMethod.Get, StarCitizenWiki.PageUrl(title));
            pageRequest.Headers.UserAgent.ParseAdd("Mozilla/5.0");
            using var pageResponse = await Http.SendAsync(pageRequest, pageCts.Token).ConfigureAwait(false);
            pageResponse.EnsureSuccessStatusCode();
            var pageBodyText = await pageResponse.Content.ReadAsStringAsync().ConfigureAwait(false);

            var foundExtract = StarCitizenWiki.ExtractPageText(JsonNode.Parse(pageBodyText));
            if (foundExtract is null)
            {
                RaiseLog($"[Info] Wiki SC : page « {title} » sans contenu exploitable (vide ou désambiguïsation).", "info");
                return "";
            }
            extract = foundExtract;
        }
        catch (Exception e)
        {
            RaiseLog($"[Info] Wiki SC : recherche de contexte ignorée pendant {step} ({e.Message}).", "info");
            return "";
        }

        RaiseLog($"[Info] Wiki SC : contexte pour « {term} » → page « {title} » ({extract.Length} caractères).", "info");
        return StarCitizenWiki.BuildReferenceBlock(title, extract);
    }

    private void RaiseLog(string message, string kind) => Log?.Invoke(this, (message, kind));
}
