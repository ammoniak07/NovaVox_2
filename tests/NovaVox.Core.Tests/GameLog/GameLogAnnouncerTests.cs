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

    [Theory]
    [InlineData(
        "CONTRAT PARTAGÉ : Niv. Jaune : Neutraliser le gang de piratage de vaisseaux. <EM4>[SP]</EM4>",
        "CONTRAT PARTAGÉ : Niv. Jaune : Neutraliser le gang de piratage de vaisseaux.")]
    [InlineData(
        "Contrat accepté : Frappe de précision sur Pete Clairmont <EM4>[SP]</EM4> <EM3>[1000 xp]</EM3>",
        "Contrat accepté : Frappe de précision sur Pete Clairmont")]
    public void CleanHudNotificationText_StripsEmphasisTags(string raw, string expected)
    {
        Assert.Equal(expected, GameLogAnnouncer.CleanHudNotificationText(raw));
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

    [Theory]
    [InlineData("Nouvel objectif", "Nouvel objectif : {name}")]
    [InlineData("Objectif terminé", "Objectif terminé : {name}")]
    [InlineData("Objectif retiré", "Objectif retiré : {name}")]
    [InlineData("CONTRAT PARTAGÉ", "CONTRAT PARTAGÉ : {name}")]
    [InlineData("Contrat accepté", "Contrat accepté : {name}")]
    [InlineData("CONTRAT TERMINÉ", "CONTRAT TERMINÉ : {name}")]
    [InlineData("CONTRAT ÉCHOUÉ", "CONTRAT ÉCHOUÉ : {name}")]
    [InlineData("ENTRÉE DU JOURNAL AJOUTÉE", "ENTRÉE DU JOURNAL AJOUTÉE : {name}")]
    public void Build_HudNotification_ObjectiveOrContractPrefix_CollapsesAcrossDifferentMissions(string prefix, string expectedTemplateKey)
    {
        var config = NewConfig();

        var first = new GameLogEvent { Type = GameLogEventTypes.HudNotification, Text = $"{prefix} : Rejoindre : Ceinture d'astéroïdes de Yela" };
        var firstResult = GameLogAnnouncer.Build(first, config);
        Assert.True(firstResult!.IsNewHudOverride);
        Assert.Equal(expectedTemplateKey, firstResult.HudOverrideKey);
        Assert.Single(config.GameLogHudOverrides);

        // Une mission complètement différente (pas juste un nom qui change) pour le
        // même préfixe ne doit toujours créer AUCUNE nouvelle entrée — c'est le
        // nombre d'entrées qui grossissait à chaque nouvelle mission auparavant.
        var second = new GameLogEvent { Type = GameLogEventTypes.HudNotification, Text = $"{prefix} : Neutraliser le gang de piratage de vaisseaux." };
        var secondResult = GameLogAnnouncer.Build(second, config);
        Assert.False(secondResult!.IsNewHudOverride);
        Assert.Single(config.GameLogHudOverrides);
        Assert.Equal($"{prefix} : Neutraliser le gang de piratage de vaisseaux.", secondResult.Text);
    }

    [Fact]
    public void Build_HudNotification_ShipChannelJoined_CollapsesAcrossDifferentShipsAndIgnoresPilotName()
    {
        var config = NewConfig();

        var first = new GameLogEvent { Type = GameLogEventTypes.HudNotification, Text = "CANAL 'Drake Cutter : Ammoniak' rejoint." };
        var firstResult = GameLogAnnouncer.Build(first, config);
        Assert.True(firstResult!.IsNewHudOverride);
        Assert.Equal("CANAL '{name}' rejoint.", firstResult.HudOverrideKey);
        Assert.Equal("CANAL 'Drake Cutter' rejoint.", firstResult.Text); // le pilote n'est jamais annoncé
        Assert.Single(config.GameLogHudOverrides);

        // Un vaisseau différent, avec un pilote différent, ne doit toujours créer aucune nouvelle entrée.
        var second = new GameLogEvent { Type = GameLogEventTypes.HudNotification, Text = "CANAL 'Anvil Paladin : Tinou214' rejoint." };
        var secondResult = GameLogAnnouncer.Build(second, config);
        Assert.False(secondResult!.IsNewHudOverride);
        Assert.Single(config.GameLogHudOverrides);
        Assert.Equal("CANAL 'Anvil Paladin' rejoint.", secondResult.Text);
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
    public void MergeLegacyNameTemplateOverrides_ReKeysEntryStillCarryingEmphasisTag()
    {
        // Texte choisi pour ne matcher AUCUN préfixe d'objectif/contrat connu
        // (voir HudTemplates) : ce test isole le nettoyage du tag d'emphase,
        // sans le regroupement par préfixe couvert par les autres tests.
        var overrides = new Dictionary<string, string>
        {
            ["Raffinage terminé à HUR-L2 <EM4>[SP]</EM4>"] = "Raffinage terminé à HUR-L2",
        };

        GameLogAnnouncer.MergeLegacyNameTemplateOverrides(overrides);

        Assert.Single(overrides);
        Assert.Equal("Raffinage terminé à HUR-L2", overrides["Raffinage terminé à HUR-L2"]);
    }

    [Theory]
    [InlineData("CANAL 'Drake Cutter : Ammoniak' rejoint.", "CANAL 'MISC Hull C : Ammoniak' rejoint.", "CANAL '{name}' rejoint.")]
    [InlineData("Vous avez quitté le CANAL 'Drake Cutter : Ammoniak'.", "Vous avez quitté le CANAL 'MISC Hull C : Ammoniak'.", "Vous avez quitté le CANAL '{name}'.")]
    public void MergeLegacyNameTemplateOverrides_ShipEntries_FallBackToDefaultWhenNoCustomizationIsSubstitutable(
        string firstRawKey, string secondRawKey, string expectedTemplateKey)
    {
        // Deux vaisseaux personnalisés chacun avec un texte DIFFÉRENT (pas le
        // texte par défaut, contrairement aux "Nouvel objectif" jamais
        // personnalisés) : aucun des deux ne peut survivre tel quel comme
        // personnalisation commune, sous peine d'annoncer le mauvais
        // vaisseau pour toutes les rencontres futures — doit retomber sur
        // le gabarit par défaut plutôt que de figer arbitrairement l'un
        // des deux.
        var overrides = new Dictionary<string, string>
        {
            [firstRawKey] = "Bienvenue à bord du Drake Cutter.",
            [secondRawKey] = "Bienvenue à bord du Hull C.",
        };

        GameLogAnnouncer.MergeLegacyNameTemplateOverrides(overrides);

        Assert.Single(overrides);
        Assert.Equal(expectedTemplateKey, overrides[expectedTemplateKey]);
    }

    [Fact]
    public void MergeLegacyNameTemplateOverrides_ShipEntries_KeepsSubstitutableCustomizationOverNonSubstitutableOne()
    {
        var overrides = new Dictionary<string, string>
        {
            ["CANAL 'Drake Cutter : Ammoniak' rejoint."] = "Bienvenue à bord du Drake Cutter.", // pas substituable
            ["CANAL 'MISC Hull C : Ammoniak' rejoint."] = "Bienvenue à bord de {name} !", // substituable
        };

        GameLogAnnouncer.MergeLegacyNameTemplateOverrides(overrides);

        Assert.Single(overrides);
        Assert.Equal("Bienvenue à bord de {name} !", overrides["CANAL '{name}' rejoint."]);
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
