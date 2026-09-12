using NovaVox.Core.Commands;
using Xunit;

namespace NovaVox.Core.Tests.Commands;

public class CommandMatcherTests
{
    private static List<VoiceCommand> SampleCommands() => new()
    {
        new VoiceCommand { Phrase = "Train d'atterrissage", Keys = "n" },
        new VoiceCommand { Phrase = "Scanner", Keys = "v" },
        new VoiceCommand { Type = "title", Phrase = "Bouclier" },
        new VoiceCommand
        {
            Phrase = "Alimentation vaisseau",
            Keys = "ctrl+n",
            Synonyms = new List<string> { "coupe l'alimentation" },
        },
    };

    [Fact]
    public void FindStrictMatch_FindsSubstringTrigger()
    {
        var idx = CommandMatcher.FindStrictMatch(SampleCommands(), "sors le train d'atterrissage");
        Assert.Equal(0, idx);
    }

    [Fact]
    public void FindStrictMatch_HandlesApostropheVariants()
    {
        // "train d atterrissage" (sans apostrophe, comme le renvoie parfois Vosk)
        var idx = CommandMatcher.FindStrictMatch(SampleCommands(), "sors le train d atterrissage");
        Assert.Equal(0, idx);
    }

    [Fact]
    public void FindStrictMatch_SkipsTitles()
    {
        var idx = CommandMatcher.FindStrictMatch(SampleCommands(), "bouclier");
        Assert.Null(idx);
    }

    [Fact]
    public void IsSignificantMatch_RequiresSubstantialShareForSingleWord()
    {
        // "système" apparaît dans une longue phrase sans rapport : ne doit
        // pas être considéré comme significatif.
        Assert.False(CommandMatcher.IsSignificantMatch("systeme", "je veux quitter le systeme pyro maintenant"));
        // Mais une expression de plusieurs mots est toujours significative.
        Assert.True(CommandMatcher.IsSignificantMatch("alimentation vaisseau", "n'importe quoi"));
    }

    [Fact]
    public void FindFuzzyMatch_ToleratesImperfectRecognition()
    {
        var (idx, ratio) = CommandMatcher.FindFuzzyMatch(SampleCommands(), "train d atterisage");
        Assert.Equal(0, idx);
        Assert.True(ratio > 0.6);
    }

    [Fact]
    public void FindFuzzyMatch_RejectsUnrelatedQuestion()
    {
        var (idx, _) = CommandMatcher.FindFuzzyMatch(SampleCommands(), "je veux quitter le systeme pyro maintenant");
        Assert.Null(idx);
    }

    [Fact]
    public void CommandKeysLabel_JoinsPrimaryAndExtraSteps()
    {
        var cmd = new VoiceCommand
        {
            Phrase = "test",
            Keys = "n",
            ExtraSteps = new List<ExtraStep> { new() { Keys = "alt+n" } },
        };
        Assert.Equal("n → alt+n", CommandMatcher.CommandKeysLabel(cmd));
    }
}
