using NovaVox.Core.Commands;
using Xunit;

namespace NovaVox.Core.Tests.Commands;

public class VoiceCommandDispatcherTests
{
    private static List<VoiceCommand> SampleCommands() => new()
    {
        new VoiceCommand { Phrase = "train d'atterrissage", Keys = "n" },
        new VoiceCommand { Phrase = "bouclier", Keys = "insert" },
    };

    [Fact]
    public void DirectCommandPhrase_ExecutesWithoutTouchingGemini()
    {
        var result = VoiceCommandDispatcher.Dispatch(
            "sors le train d'atterrissage", Array.Empty<string>(), SampleCommands(),
            geminiEnabled: true, geminiName: "Gemini", uiLanguage: "fr",
            state: new VoiceDispatcherState(), nowSeconds: 0);

        Assert.Equal(VoiceActionKind.ExecuteCommand, result.Kind);
        Assert.Equal(0, result.CommandIndex);
        Assert.False(result.ViaAi);
    }

    [Fact]
    public void FailedBestHypothesis_FallsBackToAlternative()
    {
        var result = VoiceCommandDispatcher.Dispatch(
            "un charabia sans rapport", new[] { "sors le train d'atterrissage" }, SampleCommands(),
            geminiEnabled: true, geminiName: "Gemini", uiLanguage: "fr",
            state: new VoiceDispatcherState(), nowSeconds: 0);

        Assert.Equal(VoiceActionKind.ExecuteCommand, result.Kind);
        Assert.Equal(0, result.CommandIndex);
    }

    // Référence calculée directement avec les regex Python (question
    // marker + retrait du nom de l'IA) — voir historique de session.
    [Fact]
    public void WakeWordFollowedByQuestion_AsksGeminiWhenPhraseIsInterrogative()
    {
        var result = VoiceCommandDispatcher.Dispatch(
            "c'est quoi le bouclier gemini", Array.Empty<string>(), SampleCommands(),
            geminiEnabled: true, geminiName: "Gemini", uiLanguage: "fr",
            state: new VoiceDispatcherState(), nowSeconds: 0);

        Assert.Equal(VoiceActionKind.AskGemini, result.Kind);
        Assert.Equal("c'est quoi le bouclier", result.Question);
    }

    [Fact]
    public void WakeWordFollowedByCommandPhrase_ExecutesCommandInsteadOfAskingGemini()
    {
        var result = VoiceCommandDispatcher.Dispatch(
            "gemini train atterrissage", Array.Empty<string>(), SampleCommands(),
            geminiEnabled: true, geminiName: "Gemini", uiLanguage: "fr",
            state: new VoiceDispatcherState(), nowSeconds: 0);

        // "train atterrissage" n'est pas une tournure interrogative et
        // ressemble assez à "train d'atterrissage" (fuzzy match) : exécutée
        // via l'IA plutôt que posée comme question.
        Assert.Equal(VoiceActionKind.ExecuteCommand, result.Kind);
        Assert.Equal(0, result.CommandIndex);
        Assert.True(result.ViaAi);
    }

    [Fact]
    public void WakeWordAlone_EntersAwaitingQuestionState()
    {
        var state = new VoiceDispatcherState();
        var result = VoiceCommandDispatcher.Dispatch(
            "gemini", Array.Empty<string>(), SampleCommands(),
            geminiEnabled: true, geminiName: "Gemini", uiLanguage: "fr",
            state: state, nowSeconds: 100);

        Assert.Equal(VoiceActionKind.AwaitGeminiQuestion, result.Kind);
        Assert.True(state.GeminiAwaitingQuestion);
        Assert.Equal(100, state.GeminiAwaitingSinceSeconds);
    }

    [Fact]
    public void AwaitingQuestion_NextPhraseBecomesTheQuestion()
    {
        var state = new VoiceDispatcherState { GeminiAwaitingQuestion = true, GeminiAwaitingSinceSeconds = 100 };
        var result = VoiceCommandDispatcher.Dispatch(
            "comment ça marche", Array.Empty<string>(), SampleCommands(),
            geminiEnabled: true, geminiName: "Gemini", uiLanguage: "fr",
            state: state, nowSeconds: 101);

        Assert.Equal(VoiceActionKind.AskGemini, result.Kind);
        Assert.Equal("comment ça marche", result.Question);
        Assert.False(state.GeminiAwaitingQuestion);
    }

    [Fact]
    public void AwaitingQuestion_TimesOutAfterEightSeconds()
    {
        var state = new VoiceDispatcherState { GeminiAwaitingQuestion = true, GeminiAwaitingSinceSeconds = 100 };
        var result = VoiceCommandDispatcher.Dispatch(
            "une phrase quelconque", Array.Empty<string>(), SampleCommands(),
            geminiEnabled: true, geminiName: "Gemini", uiLanguage: "fr",
            state: state, nowSeconds: 100 + VoiceCommandDispatcher.AiQuestionTimeoutSeconds + 0.1);

        Assert.Equal(VoiceActionKind.GeminiTimeout, result.Kind);
    }

    [Fact]
    public void GeminiDisabled_NeverMatchesWakeWord()
    {
        var result = VoiceCommandDispatcher.Dispatch(
            "gemini comment ça marche", Array.Empty<string>(), SampleCommands(),
            geminiEnabled: false, geminiName: "Gemini", uiLanguage: "fr",
            state: new VoiceDispatcherState(), nowSeconds: 0);

        Assert.Equal(VoiceActionKind.None, result.Kind);
    }
}
