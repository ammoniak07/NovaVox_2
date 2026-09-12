using System.Text.RegularExpressions;

namespace NovaVox.Core.Commands;

public enum VoiceActionKind
{
    /// <summary>Rien à faire : ni commande reconnue, ni question à poser.</summary>
    None,
    ExecuteCommand,
    AskGemini,
    /// <summary>Le nom de l'IA a été prononcé seul : la prochaine phrase reconnue sera la question.</summary>
    AwaitGeminiQuestion,
    /// <summary>Délai dépassé depuis AwaitGeminiQuestion : l'attente est annulée.</summary>
    GeminiTimeout,
}

public sealed class VoiceDispatchResult
{
    public required VoiceActionKind Kind { get; init; }
    public int CommandIndex { get; init; } = -1;
    public bool ViaAi { get; init; }
    public double MatchRatio { get; init; } = 1.0;
    public string? Question { get; init; }
}

/// <summary>État conversationnel maintenu entre deux appels à <see cref="VoiceCommandDispatcher.Dispatch"/> (équivalent des attributs _gemini_awaiting_* de la classe Api).</summary>
public sealed class VoiceDispatcherState
{
    public bool GeminiAwaitingQuestion { get; set; }
    public double GeminiAwaitingSinceSeconds { get; set; }
}

/// <summary>
/// Port de Api._handle_text / Api._try_execute_command_from_ai_text
/// (app.py) : décide, à partir d'une phrase reconnue par Vosk, s'il faut
/// exécuter une commande, poser une question à Gemini, ou attendre la
/// question suivante — sans effet de bord (pas d'exécution de touches, pas
/// d'appel réseau), pour rester testable indépendamment de l'audio/Vosk/
/// Gemini. L'appelant (App) traduit le <see cref="VoiceDispatchResult"/>
/// en action réelle.
/// </summary>
public static partial class VoiceCommandDispatcher
{
    public const double AiQuestionTimeoutSeconds = 8.0;

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    public static VoiceDispatchResult Dispatch(
        string text,
        IReadOnlyList<string> altTexts,
        IReadOnlyList<VoiceCommand> commands,
        bool geminiEnabled,
        string geminiName,
        string? uiLanguage,
        VoiceDispatcherState state,
        double nowSeconds)
    {
        var textNorm = text.ToLowerInvariant().Trim();
        var geminiWakeWord = geminiEnabled ? geminiName.ToLowerInvariant().Trim() : "";

        // Étape 1 : la phrase précédente était le nom de l'IA seul — celle-ci est la question.
        if (state.GeminiAwaitingQuestion)
        {
            state.GeminiAwaitingQuestion = false;
            if (nowSeconds - state.GeminiAwaitingSinceSeconds > AiQuestionTimeoutSeconds)
                return new VoiceDispatchResult { Kind = VoiceActionKind.GeminiTimeout };

            if (textNorm.Length == 0)
                return new VoiceDispatchResult { Kind = VoiceActionKind.None };

            var trimmed = text.Trim();
            var (idx, ratio) = TryMatchCommandFromAiText(trimmed, commands, uiLanguage);
            if (idx is not null)
                return new VoiceDispatchResult { Kind = VoiceActionKind.ExecuteCommand, CommandIndex = idx.Value, ViaAi = true, MatchRatio = ratio };
            return new VoiceDispatchResult { Kind = VoiceActionKind.AskGemini, Question = trimmed };
        }

        // Étape 2 : détection du nom de l'IA n'importe où dans la phrase.
        if (geminiWakeWord.Length > 0)
        {
            var pattern = $@"(?<!\w){Regex.Escape(geminiWakeWord)}(?!\w)";
            var match = Regex.Match(textNorm, pattern);
            if (match.Success)
            {
                var question = WhitespaceRegex().Replace(
                    textNorm[..match.Index] + " " + textNorm[(match.Index + match.Length)..], " ").Trim();

                if (question.Length > 0)
                {
                    var (idx, ratio) = TryMatchCommandFromAiText(question, commands, uiLanguage);
                    if (idx is not null)
                        return new VoiceDispatchResult { Kind = VoiceActionKind.ExecuteCommand, CommandIndex = idx.Value, ViaAi = true, MatchRatio = ratio };
                    return new VoiceDispatchResult { Kind = VoiceActionKind.AskGemini, Question = question };
                }

                state.GeminiAwaitingQuestion = true;
                state.GeminiAwaitingSinceSeconds = nowSeconds;
                return new VoiceDispatchResult { Kind = VoiceActionKind.AwaitGeminiQuestion };
            }
        }

        // Étape 3 : correspondance normale des commandes vocales — la
        // meilleure hypothèse Vosk d'abord, puis les hypothèses
        // alternatives (SetMaxAlternatives) si elle ne correspond à rien.
        var strictIdx = CommandMatcher.FindStrictMatch(commands, textNorm);
        if (strictIdx is null)
        {
            foreach (var alt in altTexts)
            {
                var altNorm = alt.ToLowerInvariant().Trim();
                if (altNorm.Length == 0) continue;
                strictIdx = CommandMatcher.FindStrictMatch(commands, altNorm);
                if (strictIdx is not null) break;
            }
        }

        return strictIdx is not null
            ? new VoiceDispatchResult { Kind = VoiceActionKind.ExecuteCommand, CommandIndex = strictIdx.Value, ViaAi = false, MatchRatio = 1.0 }
            : new VoiceDispatchResult { Kind = VoiceActionKind.None };
    }

    /// <summary>
    /// Port de _try_execute_command_from_ai_text : une tournure clairement
    /// interrogative n'est jamais traitée comme une commande, même si son
    /// texte contient par ailleurs la phrase d'une commande connue.
    /// </summary>
    private static (int? Index, double Ratio) TryMatchCommandFromAiText(
        string text, IReadOnlyList<VoiceCommand> commands, string? uiLanguage)
    {
        var textNorm = text.ToLowerInvariant().Trim();
        if (textNorm.Length == 0) return (null, 0.0);
        if (AiQuestionMarkers.ForLanguage(uiLanguage).IsMatch(textNorm)) return (null, 0.0);
        return CommandMatcher.FindFuzzyMatch(commands, textNorm);
    }
}
