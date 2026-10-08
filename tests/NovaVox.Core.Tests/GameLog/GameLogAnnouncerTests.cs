using NovaVox.Core.Config;
using NovaVox.Core.GameLog;
using Xunit;

namespace NovaVox.Core.Tests.GameLog;

public class GameLogAnnouncerTests
{
    private static AiConfig NewConfig() => new();

    [Theory]
    [InlineData("Schémas reçu : Ezra", "Ezra")]
    [InlineData("SCHÉMAS REÇU : Deadbolt IV Cannon", "Deadbolt IV Cannon")]
    [InlineData("Nouvel objectif : Livrer la cargaison", null)]
    public void TryExtractReceivedSchemaName_ReturnsNameOrNullWithoutSideEffects(string hudText, string? expected)
    {
        Assert.Equal(expected, GameLogAnnouncer.TryExtractReceivedSchemaName(hudText));
    }

    [Theory]
    [InlineData("CANAL 'Drake Cutter : Ammoniak' rejoint.", "Drake Cutter", true)]
    [InlineData("Vous avez quitté le CANAL 'Drake Cutter : Ammoniak'.", "Drake Cutter", false)]
    [InlineData("Nouvel objectif : Livrer la cargaison", null, null)]
    [InlineData("Un joueur a rejoint Bistic a rejoint le CANAL 'RSI Constellation Taurus : Tinou214'.", null, null)]
    public void TryExtractShipChannelEvent_ReturnsShipAndDirectionOrNullWithoutSideEffects(string hudText, string? expectedShip, bool? expectedEntered)
    {
        var result = GameLogAnnouncer.TryExtractShipChannelEvent(hudText);

        if (expectedShip is null)
        {
            Assert.Null(result);
        }
        else
        {
            Assert.NotNull(result);
            Assert.Equal(expectedShip, result!.Value.ShipName);
            Assert.Equal(expectedEntered, result!.Value.Entered);
        }
    }

    [Theory]
    [InlineData("Vous avez envoyé Droz64: 2,000,000 aUEC", 2_000_000.0)]
    [InlineData("Vous avez envoyé Zeilos: 500 aUEC.", 500.0)]
    [InlineData("Nouvel objectif : Livrer la cargaison", null)]
    public void TryExtractAuecSent_ReturnsAmountOrNullWithoutSideEffects(string hudText, double? expected)
    {
        Assert.Equal(expected, GameLogAnnouncer.TryExtractAuecSent(hudText));
    }

    [Theory]
    [InlineData("Un joueur a rejoint Bistic a rejoint le Groupe.", "Bistic")]
    [InlineData("Un joueur a rejoint Tinou214 a rejoint le Groupe.", "Tinou214")]
    [InlineData("A quitté le groupe : Tork a quitté le Groupe", null)]
    [InlineData("Groupe : Dionico31 s'est connecté.", "Dionico31")]
    [InlineData("Groupe : Dionico31 s'est déconnecté.", null)]
    [InlineData("Nouvel objectif : Livrer la cargaison", null)]
    public void TryExtractGroupMember_ReturnsNameOrNullWithoutSideEffects(string hudText, string? expected)
    {
        Assert.Equal(expected, GameLogAnnouncer.TryExtractGroupMember(hudText));
    }

    [Fact]
    public void MigrateLegacySchemaNames_StripsLeftoverEmphasisTag()
    {
        var schemas = new List<string> { "Ezra <EM3>[1000 xp]</EM3>" };

        Assert.True(GameLogAnnouncer.MigrateLegacySchemaNames(schemas));

        Assert.Equal(new[] { "Ezra" }, schemas);
    }

    [Fact]
    public void MigrateLegacySchemaNames_StripsTrailingColon()
    {
        var schemas = new List<string> { "Ezra :" };

        Assert.True(GameLogAnnouncer.MigrateLegacySchemaNames(schemas));

        Assert.Equal(new[] { "Ezra" }, schemas);
    }

    [Fact]
    public void MigrateLegacySchemaNames_MergesDuplicatesCreatedByCleaning()
    {
        var schemas = new List<string> { "Ezra <EM3>[1000 xp]</EM3>", "Ezra", "EZRA :" };

        Assert.True(GameLogAnnouncer.MigrateLegacySchemaNames(schemas));

        Assert.Single(schemas, name => string.Equals(name, "Ezra", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MigrateLegacySchemaNames_AlreadyCleanNames_ReturnsFalseAndLeavesListUntouched()
    {
        var schemas = new List<string> { "Ezra", "Deadbolt IV Cannon" };

        Assert.False(GameLogAnnouncer.MigrateLegacySchemaNames(schemas));

        Assert.Equal(new[] { "Ezra", "Deadbolt IV Cannon" }, schemas);
    }

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
    public void Build_GroupMemberConnected_SamePlayerTwiceWithoutDisconnect_SecondIsSuppressed()
    {
        // Vérifié en vrai Game.log (remontée utilisateur, 02/10/2026) : dès
        // qu'un membre rejoint/quitte le groupe, le jeu réémet "s'est
        // connecté" pour TOUS les membres déjà connectés, pas seulement
        // celui qui vient de bouger -- observé plus d'une minute après la
        // première annonce (donc hors de portée d'un simple filtre anti-
        // rafale à fenêtre glissante, voir GameLogLineProcessor).
        var config = NewConfig();
        var evt = new GameLogEvent { Type = GameLogEventTypes.HudNotification, Text = "Groupe : Dionico31 s'est connecté." };

        var first = GameLogAnnouncer.Build(evt, config);
        Assert.NotNull(first);
        Assert.Contains("Dionico31", config.ConnectedGroupMembers);

        var second = GameLogAnnouncer.Build(evt, config);
        Assert.Null(second);
    }

    [Fact]
    public void Build_GroupMemberConnected_ReannouncesAfterARealDisconnect()
    {
        var config = NewConfig();
        var connected = new GameLogEvent { Type = GameLogEventTypes.HudNotification, Text = "Groupe : Dionico31 s'est connecté." };
        var disconnected = new GameLogEvent { Type = GameLogEventTypes.HudNotification, Text = "Groupe : Dionico31 s'est déconnecté." };

        Assert.NotNull(GameLogAnnouncer.Build(connected, config));
        Assert.NotNull(GameLogAnnouncer.Build(disconnected, config));
        Assert.DoesNotContain("Dionico31", config.ConnectedGroupMembers);

        // Une vraie reconnexion après la déconnexion s'annonce de nouveau normalement.
        var reconnected = GameLogAnnouncer.Build(connected, config);
        Assert.NotNull(reconnected);
    }

    [Fact]
    public void Build_GroupMemberDisconnected_WithoutPriorConnectedTracking_IsStillAnnounced()
    {
        // Pas de "s'est connecté" jamais vu pour ce pseudo (ex. NovaVox
        // démarré après que tout le monde soit déjà connecté) : sa vraie
        // déconnexion doit quand même être annoncée, jamais supprimée --
        // seul le sens "connecté" doit l'être (voir les tests ci-dessus).
        var config = NewConfig();
        var evt = new GameLogEvent { Type = GameLogEventTypes.HudNotification, Text = "Groupe : Zeilos s'est déconnecté." };

        var first = GameLogAnnouncer.Build(evt, config);
        Assert.NotNull(first);
    }

    [Fact]
    public void Build_GroupMemberConnected_DifferentPlayersAreAllAnnounced()
    {
        var config = NewConfig();

        var first = GameLogAnnouncer.Build(new GameLogEvent { Type = GameLogEventTypes.HudNotification, Text = "Groupe : Dionico31 s'est connecté." }, config);
        var second = GameLogAnnouncer.Build(new GameLogEvent { Type = GameLogEventTypes.HudNotification, Text = "Groupe : Zeilos s'est connecté." }, config);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(2, config.ConnectedGroupMembers.Count);
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
    [InlineData("Schémas reçu", "Schémas reçu : {name}")]
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

    [Theory]
    [InlineData("Nouveau chef de groupe : Ammoniak", "Nouveau chef de groupe : Zeilos", "Nouveau chef de groupe : {name}")]
    [InlineData("Un joueur a rejoint Tinou214 a rejoint le Groupe.", "Un joueur a rejoint Bistic a rejoint le Groupe.", "Un joueur a rejoint {name} a rejoint le Groupe.")]
    [InlineData("A quitté le groupe : Tork a quitté le Groupe", "A quitté le groupe : BobbyBop a quitté le Groupe", "A quitté le groupe : {name} a quitté le Groupe")]
    [InlineData("Zeilos ! INVITATION À UN GROUPE REÇUE : Accepter l'invitation ?", "Ammoniak ! INVITATION À UN GROUPE REÇUE : Accepter l'invitation ?", "{name} ! INVITATION À UN GROUPE REÇUE : Accepter l'invitation ?")]
    [InlineData("Groupe : Dionico31 s'est connecté.", "Groupe : Zeilos s'est connecté.", "Groupe : {name} s'est connecté.")]
    [InlineData("Groupe : Dionico31 s'est déconnecté.", "Groupe : Zeilos s'est déconnecté.", "Groupe : {name} s'est déconnecté.")]
    [InlineData(
        "Initié par Spakhugar Lancement du groupe: Suivre le groupe dans l'univers persistant ?",
        "Initié par Tinou214 Lancement du groupe: Suivre le groupe dans l'univers persistant ?",
        "Initié par {name} Lancement du groupe: Suivre le groupe dans l'univers persistant ?")]
    [InlineData(
        "Lancement du groupe Initié par le chef du parti Spakhugar.",
        "Lancement du groupe Initié par le chef du parti Tinou214.",
        "Lancement du groupe Initié par le chef du parti {name}.")]
    [InlineData(
        "Vous avez envoyé Droz64: 2,000,000 aUEC",
        "Vous avez envoyé Zeilos: 500 aUEC",
        "Vous avez envoyé {name}: {montant} aUEC")]
    public void Build_HudNotification_GroupPrefix_CollapsesAcrossDifferentMembers(string firstText, string secondText, string expectedTemplateKey)
    {
        var config = NewConfig();

        var firstResult = GameLogAnnouncer.Build(new GameLogEvent { Type = GameLogEventTypes.HudNotification, Text = firstText }, config);
        Assert.True(firstResult!.IsNewHudOverride);
        Assert.Equal(expectedTemplateKey, firstResult.HudOverrideKey);
        Assert.Single(config.GameLogHudOverrides);

        var secondResult = GameLogAnnouncer.Build(new GameLogEvent { Type = GameLogEventTypes.HudNotification, Text = secondText }, config);
        Assert.False(secondResult!.IsNewHudOverride);
        Assert.Single(config.GameLogHudOverrides);
        Assert.Equal(secondText, secondResult.Text);
    }

    [Fact]
    public void Build_HudNotification_PlayerJoinedShipChannelViaGroup_CollapsesWithThreeIndependentPlaceholders()
    {
        var config = NewConfig();

        var first = new GameLogEvent
        {
            Type = GameLogEventTypes.HudNotification,
            Text = "Un joueur a rejoint Bistic a rejoint le CANAL 'RSI Constellation Taurus : Tinou214'.",
        };
        var firstResult = GameLogAnnouncer.Build(first, config);
        Assert.True(firstResult!.IsNewHudOverride);
        Assert.Equal("Un joueur a rejoint {member} a rejoint le CANAL '{ship} : {owner}'.", firstResult.HudOverrideKey);
        Assert.Equal(first.Text, firstResult.Text);
        Assert.Single(config.GameLogHudOverrides);

        // Membre, vaisseau ET propriétaire tous différents à la fois : toujours
        // aucune nouvelle entrée, et les 3 valeurs sont bien réinjectées correctement.
        var second = new GameLogEvent
        {
            Type = GameLogEventTypes.HudNotification,
            Text = "Un joueur a rejoint Torkkol a rejoint le CANAL 'RSI Perseus : Zeilos'.",
        };
        var secondResult = GameLogAnnouncer.Build(second, config);
        Assert.False(secondResult!.IsNewHudOverride);
        Assert.Single(config.GameLogHudOverrides);
        Assert.Equal(second.Text, secondResult.Text);
    }

    [Fact]
    public void MergeLegacyNameTemplateOverrides_ThreePlaceholderPattern_RequiresAllThreeToBeConsideredSubstitutable()
    {
        var overrides = new Dictionary<string, string>
        {
            // Personnalisé mais ne réintègre que le membre, pas le vaisseau ni le
            // propriétaire — pas sûr pour une future rencontre avec un autre vaisseau.
            ["Un joueur a rejoint Bistic a rejoint le CANAL 'RSI Constellation Taurus : Tinou214'."]
                = "{member} a rejoint le Constellation Taurus de Tinou214",
            // Celui-ci réintègre bien les 3.
            ["Un joueur a rejoint Torkkol a rejoint le CANAL 'RSI Perseus : Zeilos'."]
                = "{member} a rejoint le {ship} de {owner}",
        };

        GameLogAnnouncer.MergeLegacyNameTemplateOverrides(overrides);

        Assert.Single(overrides);
        Assert.Equal(
            "{member} a rejoint le {ship} de {owner}",
            overrides["Un joueur a rejoint {member} a rejoint le CANAL '{ship} : {owner}'."]);
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
    public void Build_ZoneChange_KnownDestination_NeverRegistersRedundantAlias()
    {
        // "OOC_Stanton_1_Hurston" est déjà dans GameLogDestinations.KnownLocationAliases
        // ("Hurston") : ne doit JAMAIS créer d'entrée personnelle, même à la
        // première rencontre — sinon "Alias de destinations" grossit d'une
        // entrée par destination croisée, y compris celles déjà parfaitement
        // gérées d'origine (voir remontée utilisateur : "pourquoi l'app
        // m'ajoute encore des noms").
        var config = NewConfig();
        var evt = new GameLogEvent { Type = GameLogEventTypes.ZoneChange, Zone = "OOC_Stanton_1_Hurston" };

        var result = GameLogAnnouncer.Build(evt, config);

        Assert.NotNull(result);
        Assert.Equal("Arrivée à destination : Hurston", result!.Text);
        Assert.False(result.IsNewDestinationAlias);
        Assert.Empty(config.GameLogDestinationAliases);
        Assert.Equal("Hurston", result.ResolvedZone);
    }

    [Fact]
    public void Build_ZoneChange_UnknownDestination_StillRegistersAliasOnFirstSighting()
    {
        // À l'inverse : une destination qu'AUCUN mécanisme intégré ne sait
        // résoudre (ni catalogue, ni point de saut, ni format OOC_...) doit
        // toujours être ajoutée à "Alias de destinations", prête à être
        // renommée — c'est le seul cas où une entrée a un intérêt réel.
        var config = NewConfig();
        var evt = new GameLogEvent { Type = GameLogEventTypes.ZoneChange, Zone = "Some_Random_Depot_Site" };

        var result = GameLogAnnouncer.Build(evt, config);

        Assert.NotNull(result);
        Assert.True(result!.IsNewDestinationAlias);
        Assert.True(result.UnresolvedDestinationWarning);
        Assert.True(config.GameLogDestinationAliases.ContainsKey("some random depot site"));
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

    // Ligne réelle vérifiée dans un vrai Game.log (30/09/2026) — la
    // juridiction change indépendamment du nom de la zone/station elle-même
    // (ex. Megumi Ravitaillement peut se trouver sous juridiction "Rough &
    // Ready"), d'où une ligne dédiée dans l'overlay plutôt qu'une fusion
    // avec Zone.
    [Fact]
    public void Build_HudNotification_JuridictionExtractsResolvedJurisdiction()
    {
        var config = NewConfig();
        var evt = new GameLogEvent { Type = GameLogEventTypes.HudNotification, Text = "JURIDICTION : Rough & Ready: " };

        var result = GameLogAnnouncer.Build(evt, config);

        Assert.NotNull(result);
        Assert.Equal("Rough & Ready", result!.ResolvedJurisdiction);
    }

    [Fact]
    public void Build_HudNotification_NoJurisdiction_ExtractsVerbatim()
    {
        var config = NewConfig();
        var evt = new GameLogEvent { Type = GameLogEventTypes.HudNotification, Text = "JURIDICTION : Aucune juridiction: " };

        var result = GameLogAnnouncer.Build(evt, config);

        Assert.NotNull(result);
        Assert.Equal("Aucune juridiction", result!.ResolvedJurisdiction);
    }

    [Fact]
    public void Build_HudNotification_UnrelatedText_ResolvedJurisdictionIsNull()
    {
        var config = NewConfig();
        var evt = new GameLogEvent { Type = GameLogEventTypes.HudNotification, Text = "Nouvel objectif : Livrer le colis" };

        var result = GameLogAnnouncer.Build(evt, config);

        Assert.NotNull(result);
        Assert.Null(result!.ResolvedJurisdiction);
    }

    // Lignes réelles vérifiées dans un vrai Game.log (30/09/2026).
    [Fact]
    public void Build_HudNotification_ArmisticeEntered_ResolvedArmisticeIsTrue()
    {
        var config = NewConfig();
        var evt = new GameLogEvent { Type = GameLogEventTypes.HudNotification, Text = "VOUS ENTREZ EN ZONE D'ARMISTICE - COMBAT INTERDIT: " };

        var result = GameLogAnnouncer.Build(evt, config);

        Assert.NotNull(result);
        Assert.True(result!.ResolvedArmistice);
    }

    [Fact]
    public void Build_HudNotification_ArmisticeLeft_ResolvedArmisticeIsFalse()
    {
        var config = NewConfig();
        var evt = new GameLogEvent { Type = GameLogEventTypes.HudNotification, Text = "VOUS QUITTEZ LA ZONE D'ARMISTICE - PRUDENCE EST MÈRE DE SÛRETÉ: " };

        var result = GameLogAnnouncer.Build(evt, config);

        Assert.NotNull(result);
        Assert.False(result!.ResolvedArmistice);
    }

    [Fact]
    public void Build_HudNotification_UnrelatedText_ResolvedArmisticeIsNull()
    {
        var config = NewConfig();
        var evt = new GameLogEvent { Type = GameLogEventTypes.HudNotification, Text = "Nouvel objectif : Livrer le colis" };

        var result = GameLogAnnouncer.Build(evt, config);

        Assert.NotNull(result);
        Assert.Null(result!.ResolvedArmistice);
    }

    [Fact]
    public void Build_HudNotification_SchemaReceived_RegistersNameOnFirstSighting()
    {
        var config = NewConfig();
        var evt = new GameLogEvent { Type = GameLogEventTypes.HudNotification, Text = "Schémas reçu : Ezra" };

        var result = GameLogAnnouncer.Build(evt, config);

        Assert.NotNull(result);
        Assert.Equal("Ezra", result!.ReceivedSchemaName);
        Assert.Contains("Ezra", config.SchemasReceived);
        Assert.Contains("Ezra", config.SchemasUnseen);
        Assert.Equal("Schémas reçu : Ezra", result.Text); // toujours annoncé normalement, via le gabarit générique
    }

    [Fact]
    public void Build_HudNotification_SchemaReceived_SameNameAgain_DoesNotDuplicateOrReportAsNew()
    {
        var config = NewConfig();
        GameLogAnnouncer.Build(new GameLogEvent { Type = GameLogEventTypes.HudNotification, Text = "Schémas reçu : Ezra" }, config);

        var result = GameLogAnnouncer.Build(new GameLogEvent { Type = GameLogEventTypes.HudNotification, Text = "Schémas reçu : ezra" }, config); // casse différente

        Assert.NotNull(result);
        Assert.Null(result!.ReceivedSchemaName);
        Assert.Single(config.SchemasReceived);
    }

    [Fact]
    public void Build_HudNotification_SchemaReceived_DifferentNames_BothRegistered()
    {
        var config = NewConfig();
        GameLogAnnouncer.Build(new GameLogEvent { Type = GameLogEventTypes.HudNotification, Text = "Schémas reçu : Ezra" }, config);
        GameLogAnnouncer.Build(new GameLogEvent { Type = GameLogEventTypes.HudNotification, Text = "Schémas reçu : Mantis" }, config);

        Assert.Equal(new[] { "Ezra", "Mantis" }, config.SchemasReceived);
    }

    [Fact]
    public void Build_HudNotification_UnrelatedText_ReceivedSchemaNameIsNull()
    {
        var config = NewConfig();
        var evt = new GameLogEvent { Type = GameLogEventTypes.HudNotification, Text = "Nouvel objectif : Livrer le colis" };

        var result = GameLogAnnouncer.Build(evt, config);

        Assert.NotNull(result);
        Assert.Null(result!.ReceivedSchemaName);
        Assert.Empty(config.SchemasReceived);
    }

    [Fact]
    public void Build_NicknameDetected_ReturnsNull()
    {
        var config = NewConfig();
        var evt = new GameLogEvent { Type = GameLogEventTypes.NicknameDetected, Nickname = "Ammoniak007" };
        Assert.Null(GameLogAnnouncer.Build(evt, config));
    }

    [Fact]
    public void MergeLegacyNameTemplateOverrides_RealWorldGroupEntries_CollapseIntoExpectedTemplates()
    {
        // Extrait fidèle d'un vrai game_log_hud_overrides utilisateur (avant ce
        // regroupement) : plusieurs membres/vaisseaux différents pour les mêmes
        // motifs de groupe, certains édités à la main avec une coquille
        // (guillemet simple en trop en fin de valeur) — ne doit jamais faire
        // planter la fusion, seulement la clé compte pour détecter le motif.
        var overrides = new Dictionary<string, string>
        {
            ["Nouveau chef de groupe : Ammoniak"] = "Ammoniak est le Nouveau chef de groupe",
            ["Nouveau chef de groupe : Zeilos"] = "Zeilos est le Nouveau chef de groupe",
            ["Un joueur a rejoint Tinou214 a rejoint le Groupe."] = "Tinou214 a rejoint le Groupe.",
            ["Un joueur a rejoint Bistic a rejoint le Groupe."] = "Bistic a rejoint le Groupe.",
            ["A quitté le groupe : Tork a quitté le Groupe"] = "Tork a quitté le Groupe",
            ["A quitté le groupe : BobbyBop a quitté le Groupe"] = "BobbyBop a quitté le Groupe",
            ["A quitté le groupe : 1CC-Luche08 a quitté le Groupe"] = "1CC-Luche08 a quitté le Groupe",
            ["A quitté le groupe : Zeilos a quitté le Groupe"] = "Zeilos a quitté le Groupe",
            ["A quitté le groupe : Bistic a quitté le Groupe"] = "Bistic a quitté le Groupe",
            ["Un joueur a rejoint Bistic a rejoint le CANAL 'RSI Constellation Taurus : Tinou214'."]
                = "Bistic a rejoint Bistic a rejoint le Constellation Taurus de Tinou214",
            ["Un joueur a rejoint 1CC-Luche08 a rejoint le CANAL 'RSI Constellation Taurus : Tinou214'."]
                = "Un joueur a rejoint 1CC-Luche08 a rejoint le Constellation Taurus de Tinou214",
            ["Un joueur a rejoint Torkkol a rejoint le CANAL 'RSI Perseus : Zeilos'."]
                = "Torkkol a rejoint le Perseus de Zeilos",
            ["A quitté le groupe : Torkkol a quitté le CANAL 'RSI Perseus : Zeilos'"]
                = "A quitté le groupe : Torkkol a quitté le CANAL 'RSI Perseus : Zeilos'",
            ["A quitté le groupe : BobbyBop a quitté le CANAL 'RSI Perseus : Zeilos'"]
                = "A quitté le groupe : BobbyBop a quitté le CANAL 'RSI Perseus : Zeilos'",
            ["A quitté le groupe : 1CC-Luche08 a quitté le CANAL 'RSI Perseus : Zeilos'"]
                = "A quitté le groupe : 1CC-Luche08 a quitté le CANAL 'RSI Perseus : Zeilos'",
            ["A quitté le groupe : 1CC-Luche08 a quitté le CANAL 'RSI Constellation Taurus : Tinou214'"]
                = "1CC-Luche08 a quitté le RSI Constellation Taurus de Tinou214'", // coquille : guillemet en trop
            ["A quitté le groupe : Bistic a quitté le CANAL 'RSI Constellation Taurus : Tinou214'"]
                = "Bistic a quitté le Constellation Taurus de Tinou214'", // idem
            ["A quitté le groupe : Bistic a quitté le CANAL 'RSI Perseus : Zeilos'"]
                = "Bistic a quitté le Perseus de Zeilos",
            // Connexions au groupe (remontée utilisateur suivante) : une entrée
            // par membre reconnecté, même motif que "Nouveau chef de groupe".
            ["Groupe : Dionico31 s'est connecté."] = "Groupe : Dionico31 s'est connecté.",
            ["Groupe : Zeilos s'est connecté."] = "Groupe : Zeilos s'est connecté.",
            ["Groupe : Torkkol s'est connecté."] = "Groupe : Torkkol s'est connecté.",
            ["Groupe : 1CC-Luche08 s'est connecté."] = "Groupe : 1CC-Luche08 s'est connecté.",
            // Lancement de groupe dans l'univers persistant (remontée utilisateur suivante).
            ["Initié par Spakhugar Lancement du groupe: Suivre le groupe dans l'univers persistant ?"]
                = "Initié par Spakhugar Lancement du groupe: Suivre le groupe dans l'univers persistant ?",
            ["Lancement du groupe Initié par le chef du parti Spakhugar."]
                = "Lancement du groupe Initié par le chef du parti Spakhugar.",
        };

        GameLogAnnouncer.MergeLegacyNameTemplateOverrides(overrides);

        // 24 entrées héritées -> 8 motifs de groupe distincts (chef, rejoint
        // groupe, quitté groupe, rejoint canal de vaisseau via groupe, quitté
        // canal de vaisseau via groupe, connecté, les deux formes de lancement
        // de groupe dans l'univers persistant).
        Assert.Equal(8, overrides.Count);
        Assert.Contains("Nouveau chef de groupe : {name}", overrides.Keys);
        Assert.Contains("Un joueur a rejoint {name} a rejoint le Groupe.", overrides.Keys);
        Assert.Contains("A quitté le groupe : {name} a quitté le Groupe", overrides.Keys);
        Assert.Contains("Un joueur a rejoint {member} a rejoint le CANAL '{ship} : {owner}'.", overrides.Keys);
        Assert.Contains("A quitté le groupe : {member} a quitté le CANAL '{ship} : {owner}'", overrides.Keys);
        Assert.Contains("Groupe : {name} s'est connecté.", overrides.Keys);
        Assert.Contains("Initié par {name} Lancement du groupe: Suivre le groupe dans l'univers persistant ?", overrides.Keys);
        Assert.Contains("Lancement du groupe Initié par le chef du parti {name}.", overrides.Keys);

        // Aucune des valeurs héritées ci-dessus ne réintègre les 3 réservoirs à
        // la fois : doit retomber sur le gabarit neutre, jamais figer un
        // membre/vaisseau/propriétaire précis pour toutes les rencontres futures.
        Assert.Equal(
            "Un joueur a rejoint {member} a rejoint le CANAL '{ship} : {owner}'.",
            overrides["Un joueur a rejoint {member} a rejoint le CANAL '{ship} : {owner}'."]);
        Assert.Equal(
            "A quitté le groupe : {member} a quitté le CANAL '{ship} : {owner}'",
            overrides["A quitté le groupe : {member} a quitté le CANAL '{ship} : {owner}'"]);
    }
}
