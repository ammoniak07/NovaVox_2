using NovaVox.Core.GameLog;
using Xunit;

namespace NovaVox.Core.Tests.GameLog;

public class GameStatePromptTests
{
    [Fact]
    public void ToPromptBlock_EmptyWhenNoZoneKnown()
    {
        Assert.Equal("", GameStatePrompt.ToPromptBlock(new GameLogState()));
    }

    [Fact]
    public void ToPromptBlock_IncludesCurrentZone()
    {
        var block = GameStatePrompt.ToPromptBlock(new GameLogState { CurrentZone = "Everus Harbor" });
        Assert.Contains("Everus Harbor", block);
        Assert.StartsWith("\n\nÉtat de la partie en cours", block);
    }

    [Fact]
    public void ToPromptBlock_EmptyWhenStateNull()
    {
        Assert.Equal("", GameStatePrompt.ToPromptBlock(null));
    }
}
