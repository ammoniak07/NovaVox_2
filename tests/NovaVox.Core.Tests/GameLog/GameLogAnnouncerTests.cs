using NovaVox.Core.Config;
using NovaVox.Core.GameLog;
using Xunit;

namespace NovaVox.Core.Tests.GameLog;

public class GameLogAnnouncerTests
{
    private static AiConfig NewConfig() => new();

    [Fact]
    public void CleanHudNotificationText_StripsTrailingColonAndCollapsesNewlines()
    {
        var result = GameLogAnnouncer.CleanHudNotificationText("VOUS QUITTEZ LA ZONE\n D'ARMISTICE :  ");
        Assert.Equal("VOUS QUITTEZ LA ZONE D'ARMISTICE", result);
    }

    [Fact]
    public void Build_HudNotification_RegistersNewOverrideAndSpeaksRawTextFirstTime()
    {
        var config = NewConfig();
        var evt = new GameLogEvent { Type = GameLogEventTypes.HudNotification, Text = "Bienvenue à bord" };

        var result = GameLogAnnouncer.Build(evt, config);

        Assert.NotNull(result);
        Assert.Equal("Bienvenue à bord", result!.Text);
        Assert.True(result.IsNewHudOverride);
        Assert.Null(result.RawHudText); // spoken == raw the first time, no override yet
        Assert.Equal("Bienvenue à bord", config.GameLogHudOverrides["Bienvenue à bord"]);
    }

    [Fact]
    public void Build_HudNotification_UsesCustomOverrideOnSecondSighting()
    {
        var config = NewConfig();
        config.GameLogHudOverrides["Bienvenue à bord"] = "Bienvenue capitaine";
        var evt = new GameLogEvent { Type = GameLogEventTypes.HudNotification, Text = "Bienvenue à bord" };

        var result = GameLogAnnouncer.Build(evt, config);

        Assert.NotNull(result);
        Assert.Equal("Bienvenue capitaine", result!.Text);
        Assert.False(result.IsNewHudOverride);
        Assert.Equal("Bienvenue à bord", result.RawHudText); // différent du texte annoncé -> affiché en plus
    }

    [Fact]
    public void Build_HudNotification_CrimeReport_NormalizesPlayerNameIntoTemplate()
    {
        var config = NewConfig();
        var evt = new GameLogEvent
        {
            Type = GameLogEventTypes.HudNotification,
            Text = "Brick_Century a commis Agression aggravée contre vous Appuyez sur 'Accepter' pour signaler le crime, sinon celui-ci sera pardonné.",
        };

        var result = GameLogAnnouncer.Build(evt, config);

        Assert.NotNull(result);
        Assert.True(result!.IsNewHudOverride);
        Assert.Equal(
            "{name} a commis Agression aggravée contre vous Appuyez sur 'Accepter' pour signaler le crime, sinon celui-ci sera pardonné.",
            result.HudOverrideKey);
        // La première rencontre reconstruit le texte brut exact (le nom est réinjecté) : rien de "différent" à signaler.
        Assert.Null(result.RawHudText);
        Assert.Contains("Brick_Century a commis Agression aggravée", result.Text);
    }

    [Fact]
    public void Build_HudNotification_CrimeReport_SameCrimeDifferentPlayer_DoesNotRegisterNewOverride()
    {
        var config = NewConfig();
        var first = new GameLogEvent
        {
            Type = GameLogEventTypes.HudNotification,
            Text = "Brick_Century a commis Agression aggravée contre vous Appuyez sur 'Accepter' pour signaler le crime, sinon celui-ci sera pardonné.",
        };
        GameLogAnnouncer.Build(first, config);
        Assert.Single(config.GameLogHudOverrides);

        var second = new GameLogEvent
        {
            Type = GameLogEventTypes.HudNotification,
            Text = "BonCloud a commis Agression aggravée contre vous Appuyez sur 'Accepter' pour signaler le crime, sinon celui-ci sera pardonné.",
        };
        var result = GameLogAnnouncer.Build(second, config);

        Assert.NotNull(result);
        Assert.False(result!.IsNewHudOverride);
        Assert.Single(config.GameLogHudOverrides); // toujours une seule entrée, pas une par nom
        Assert.Contains("BonCloud a commis Agression aggravée", result.Text);
    }

    [Fact]
    public void Build_HudNotification_CrimeReport_CustomTemplateSubstitutesNewName()
    {
        var config = NewConfig();
        config.GameLogHudOverrides["{name} a commis Vol contre vous."] = "Attention, {name} vous a volé quelque chose !";
        var evt = new GameLogEvent { Type = GameLogEventTypes.HudNotification, Text = "MAEDAYMAEDAY a commis Vol contre vous." };

        var result = GameLogAnnouncer.Build(evt, config);

        Assert.NotNull(result);
        Assert.False(result!.IsNewHudOverride);
        Assert.Equal("Attention, MAEDAYMAEDAY vous a volé quelque chose !", result.Text);
    }

    [Fact]
    public void Build_HudNotification_EmptyAfterCleaning_ReturnsNull()
    {
        var config = NewConfig();
        var evt = new GameLogEvent { Type = GameLogEventTypes.HudNotification, Text = "   " };
        Assert.Null(GameLogAnnouncer.Build(evt, config));
    }

    [Theory]
    [InlineData("AMI AJOUTÉ ! Tinou214", "AMI AJOUTÉ ! {name}")]
    [InlineData("Calibration du voyage quantique démarrée par Tinou214.", "Calibration du voyage quantique démarrée par {name}.")]
    [InlineData("Calibration du voyage quantique terminée par Tinou214.", "Calibration du voyage quantique terminée par {name}.")]
    public void Build_HudNotification_KnownNamePattern_NormalizesIntoTemplate(string rawText, string expectedTemplateKey)
    {
        var config = NewConfig();
        var evt = new GameLogEvent { Type = GameLogEventTypes.HudNotification, Text = rawText };

        var result = GameLogAnnouncer.Build(evt, config);

        Assert.NotNull(result);
        Assert.True(result!.IsNewHudOverride);
        Assert.Equal(expectedTemplateKey, result.HudOverrideKey);
        Assert.Equal(rawText, result.Text); // première rencontre : nom réinjecté tel quel

        // Un autre nom pour le même motif ne doit pas créer une deuxième entrée.
        var second = new GameLogEvent { Type = GameLogEventTypes.HudNotification, Text = rawText.Replace("Tinou214", "AutrePilote") };
        var secondResult = GameLogAnnouncer.Build(second, config);
        Assert.False(secondResult!.IsNewHudOverride);
        Assert.Single(config.GameLogHudOverrides);
    }

    [Fact]
    public void MergeLegacyNameTemplateOverrides_PromotesRawKeyToTemplateWhenTemplateAbsent()
    {
        var overrides = new Dictionary<string, string>
        {
            ["MAEDAYMAEDAY a commis Vol contre vous."] = "{name} a commis Vol contre vous.",
        };

        GameLogAnnouncer.MergeLegacyNameTemplateOverrides(overrides);

        Assert.Single(overrides);
        Assert.True(overrides.ContainsKey("{name} a commis Vol contre vous."));
        Assert.False(overrides.ContainsKey("MAEDAYMAEDAY a commis Vol contre vous."));
    }

    [Fact]
    public void MergeLegacyNameTemplateOverrides_DropsRawKeyWithoutOverwritingExistingTemplate()
    {
        var overrides = new Dictionary<string, string>
        {
            ["{name} a commis Vol contre vous."] = "Attention, {name} vous a volé quelque chose !",
            ["MAEDAYMAEDAY a commis Vol contre vous."] = "MAEDAYMAEDAY a commis Vol contre vous.",
        };

        GameLogAnnouncer.MergeLegacyNameTemplateOverrides(overrides);

        Assert.Single(overrides);
        Assert.Equal("Attention, {name} vous a volé quelque chose !", overrides["{name} a commis Vol contre vous."]);
    }

    [Fact]
    public void MergeLegacyNameTemplateOverrides_LeavesUnrelatedEntriesUntouched()
    {
        var overrides = new Dictionary<string, string> { ["Bienvenue à bord"] = "Bienvenue capitaine" };

        GameLogAnnouncer.MergeLegacyNameTemplateOverrides(overrides);

        Assert.Single(overrides);
        Assert.Equal("Bienvenue capitaine", overrides["Bienvenue à bord"]);
    }

    [Fact]
    public void Build_ZoneChange_RegistersDestinationAliasOnFirstSighting()
    {
        var config = NewConfig();
        var evt = new GameLogEvent { Type = GameLogEventTypes.ZoneChange, Zone = "OOC_Stanton_1_Hurston" };

        var result = GameLogAnnouncer.Build(evt, config);

        Assert.NotNull(result);
        Assert.Equal("Arrivée à destination : Hurston", result!.Text);
        Assert.True(result.IsNewDestinationAlias);
        Assert.True(config.GameLogDestinationAliases.ContainsKey("ooc stanton 1 hurston"));
        Assert.Equal("Hurston", result.ResolvedZone);
    }

    [Fact]
    public void Build_ZoneChange_UnknownDestination_UsesNoZoneKey()
    {
        var config = NewConfig();
        var evt = new GameLogEvent { Type = GameLogEventTypes.ZoneChange, Zone = "" };

        var result = GameLogAnnouncer.Build(evt, config);

        Assert.NotNull(result);
        Assert.Equal("zone_change_no_zone", result!.Key);
        Assert.Equal("Arrivée à destination", result.Text);
        Assert.Null(result.ResolvedZone);
    }

    [Fact]
    public void Build_NicknameDetected_ReturnsNull()
    {
        var config = NewConfig();
        var evt = new GameLogEvent { Type = GameLogEventTypes.NicknameDetected, Nickname = "Ammoniak007" };
        Assert.Null(GameLogAnnouncer.Build(evt, config));
    }
}
