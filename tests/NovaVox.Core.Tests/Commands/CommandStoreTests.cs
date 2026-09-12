using NovaVox.Core.Commands;
using Xunit;

namespace NovaVox.Core.Tests.Commands;

public class CommandStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("novavox-tests-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void LoadCommands_ReturnsDefaultsWhenNoFile()
    {
        var store = new CommandStore(_dir);
        var commands = store.LoadCommands();
        Assert.Equal(CommandStore.DefaultCommands().Select(c => c.Phrase), commands.Select(c => c.Phrase));
    }

    [Fact]
    public void SaveThenLoad_RoundTripsCommandsAndTitles()
    {
        var store = new CommandStore(_dir);
        var commands = new List<VoiceCommand>
        {
            new() { Type = "title", Phrase = "Bouclier" },
            new()
            {
                Phrase = "train d'atterrissage", Keys = "n", Hold = true, RepeatCount = 3, RepeatDelay = 0.25,
                Synonyms = new() { "train d atterrissage" },
                ExtraSteps = new() { new ExtraStep { Keys = "alt+n", DelayBefore = 0.5 } },
                TriggerHotkey = "joy:{\"name\":\"VirPil\",\"guid\":\"abc\",\"button\":5}",
            },
        };
        store.SaveCommands(commands, mirrorToProfile: false);

        var reloaded = new CommandStore(_dir).LoadCommands();
        Assert.Equal(2, reloaded.Count);
        Assert.True(reloaded[0].IsTitle);
        Assert.Equal("Bouclier", reloaded[0].Phrase);
        Assert.False(reloaded[1].IsTitle);
        Assert.Equal("n", reloaded[1].Keys);
        Assert.True(reloaded[1].Hold);
        Assert.Equal(3, reloaded[1].RepeatCount);
        Assert.Equal(0.25, reloaded[1].RepeatDelay);
        Assert.Single(reloaded[1].ExtraSteps);
        Assert.Equal("alt+n", reloaded[1].ExtraSteps[0].Keys);
        Assert.Equal(0.5, reloaded[1].ExtraSteps[0].DelayBefore);
        Assert.Equal("joy:{\"name\":\"VirPil\",\"guid\":\"abc\",\"button\":5}", reloaded[1].TriggerHotkey);
    }

    [Fact]
    public void SaveThenLoad_TriggerHotkeyDefaultsToNullWhenUnset()
    {
        var store = new CommandStore(_dir);
        store.SaveCommands(new List<VoiceCommand> { new() { Phrase = "x", Keys = "n" } }, mirrorToProfile: false);

        var reloaded = new CommandStore(_dir).LoadCommands();
        Assert.Null(reloaded[0].TriggerHotkey);
    }

    [Fact]
    public void NormalizeCommands_ClampsRepeatCountAndDelay()
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse(
            """[{"phrase":"x","keys":"n","repeat_count":999,"repeat_delay":-5}]""");
        var commands = CommandStore.NormalizeCommands(json);
        Assert.Equal(50, commands[0].RepeatCount);
        Assert.Equal(0.0, commands[0].RepeatDelay);
    }

    [Fact]
    public void NormalizeCommands_DropsEntriesMissingPhraseOrKeys()
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse("""[{"phrase":"x"}, {"keys":"n"}]""");
        var commands = CommandStore.NormalizeCommands(json);
        Assert.Empty(commands);
    }

    [Fact]
    public void ProfileLifecycle_CreateSwitchListDelete()
    {
        var store = new CommandStore(_dir);
        var migratedId = store.EnsureProfilesMigrated();
        Assert.NotNull(migratedId);
        store.ActiveProfileId = migratedId;

        var newId = store.NewProfileId();
        store.WriteProfile(newId, "Combat", new List<VoiceCommand> { new() { Phrase = "tirer", Keys = "space" } });

        var profiles = store.ListProfiles();
        Assert.Equal(2, profiles.Count);
        Assert.Contains(profiles, p => p.Name == "Combat" && p.Count == 1);

        var (name, commands) = store.ReadProfile(newId);
        Assert.Equal("Combat", name);
        Assert.Single(commands);
    }

    [Fact]
    public void SaveCommands_DoesNotRecreateDeletedActiveProfile()
    {
        var store = new CommandStore(_dir) { ActiveProfileId = "ghost" };
        store.SaveCommands(new List<VoiceCommand> { new() { Phrase = "x", Keys = "n" } });
        Assert.False(File.Exists(store.ProfilePath("ghost")));
    }
}
