using NovaVox.Core.GameLog;
using Xunit;

namespace NovaVox.Core.Tests.GameLog;

public class GameLogLineProcessorTests
{
    [Fact]
    public void ProcessLine_UpdatesLastLineTimestamp_EvenForAnUnrecognizedLine()
    {
        var processor = new GameLogLineProcessor();

        processor.ProcessLine("<2026-09-20T18:00:00.000Z> [Notice] <SomeUnknownTag> rien de reconnu ici");

        Assert.Equal(DateTimeOffset.Parse("2026-09-20T18:00:00.000Z"), processor.State.LastLineTimestamp);
    }

    [Fact]
    public void ProcessLine_LastLineTimestamp_AdvancesWithEachNewLine()
    {
        var processor = new GameLogLineProcessor();

        processor.ProcessLine("<2026-09-20T18:00:00.000Z> [Notice] <A> première ligne");
        processor.ProcessLine("<2026-09-20T18:05:00.000Z> [Notice] <B> deuxième ligne");

        Assert.Equal(DateTimeOffset.Parse("2026-09-20T18:05:00.000Z"), processor.State.LastLineTimestamp);
    }

    [Fact]
    public void ProcessLine_LineWithoutTimestamp_LeavesLastLineTimestampUnchanged()
    {
        var processor = new GameLogLineProcessor();
        processor.ProcessLine("<2026-09-20T18:00:00.000Z> [Notice] <A> première ligne");

        processor.ProcessLine("ligne sans horodatage du tout");

        Assert.Equal(DateTimeOffset.Parse("2026-09-20T18:00:00.000Z"), processor.State.LastLineTimestamp);
    }

    [Fact]
    public void ParseLineTimestamp_ValidLine_ReturnsParsedTimestamp()
    {
        Assert.Equal(
            DateTimeOffset.Parse("2026-09-20T18:00:00.000Z"),
            GameLogLineProcessor.ParseLineTimestamp("<2026-09-20T18:00:00.000Z> [Notice] <A> peu importe"));
    }

    [Fact]
    public void ParseLineTimestamp_LineWithoutTimestamp_ReturnsNull()
    {
        Assert.Null(GameLogLineProcessor.ParseLineTimestamp("pas d'horodatage ici"));
    }

    // Séquence de lignes et événements attendus vérifiés directement avec
    // GameLogWatcher._process_line (game_log_watcher.py) — voir historique
    // de session pour la commande utilisée.
    [Fact]
    public void FullRouteSequence_EmitsRouteSetThenZoneChangeAndDedupsRepeat()
    {
        var processor = new GameLogLineProcessor();
        var events = new List<GameLogEvent>();
        void Feed(string line)
        {
            var evt = processor.ProcessLine(line);
            if (evt is not null) events.Add(evt);
        }

        Feed("<2026-08-15T16:10:00.000Z> ...OnPlayerSelectedQuantumTarget|Player has selected point ObjectContainer_RestStop as their destination, routing locally [Team][QuantumTravel]");
        Feed("<2026-08-15T16:10:00.000Z> ...CalculateRoute|Projected Start Location is Lorville for route to destination ObjectContainer_RestStop [Team][QuantumTravel]");
        Feed("<2026-08-15T16:10:00.000Z> ...Successfully calculated route to ObjectContainer_RestStop fuel estimate 12.5");
        Feed("<2026-08-15T16:10:00.000Z> <Quantum Drive Arrived - Arrived at Final Destination> blah has arrived at final destination [Team][QuantumTravel]");
        Feed("<2026-08-15T16:10:00.000Z> ...Successfully calculated route to ObjectContainer_RestStop fuel estimate 12.5"); // répétition : dédoublonnée

        Assert.Equal(2, events.Count);

        Assert.Equal(GameLogEventTypes.RouteSet, events[0].Type);
        Assert.Equal("ObjectContainer_RestStop", events[0].Destination);
        Assert.Null(events[0].ObstructionLabel);
        Assert.Equal("Lorville", events[0].StartLocation);

        Assert.Equal(GameLogEventTypes.ZoneChange, events[1].Type);
        Assert.Equal("ObjectContainer_RestStop", events[1].Zone);
        Assert.Equal("Lorville", events[1].StartLocation);

        Assert.True(processor.State.Connected);
        Assert.Equal("ObjectContainer_RestStop", processor.State.CurrentZone);
    }

    [Fact]
    public void NewTargetSelection_ClearsPendingObstructionAndStartLocation()
    {
        var processor = new GameLogLineProcessor();
        processor.ProcessLine("<2026-08-15T16:10:00.000Z> ...CalculateRoute|Projected Start Location is Lorville for route to destination Foo [Team][QuantumTravel]");
        processor.ProcessLine("<2026-08-15T16:10:00.000Z> ...routing from Lorville to ArcCorp Obstructing Entity OOC_Stanton_3");
        // Nouvelle sélection : doit remettre à zéro l'obstruction/départ mémorisés.
        processor.ProcessLine("<2026-08-15T16:10:00.000Z> ...OnPlayerSelectedQuantumTarget|Player has selected point Bar as their destination, routing locally");
        var evt = processor.ProcessLine("<2026-08-15T16:10:00.000Z> ...Successfully calculated route to Bar fuel estimate 1.0");

        Assert.NotNull(evt);
        Assert.Null(evt!.ObstructionLabel);
        Assert.Null(evt.StartLocation);
    }

    [Fact]
    public void HudNotification_SingleLine_EmitsImmediately()
    {
        var processor = new GameLogLineProcessor();
        var evt = processor.ProcessLine(
            "<2026-08-15T16:10:00.000Z> [Notice] <SHUDEvent_OnNotification> Added notification \"Nouvel objectif : Livrer la cargaison\" [20] to queue. New queue size: 2, MissionId: [abc]");
        Assert.NotNull(evt);
        Assert.Equal(GameLogEventTypes.HudNotification, evt!.Type);
        Assert.Equal("Nouvel objectif : Livrer la cargaison", evt.Text);
    }

    [Fact]
    public void HudNotification_MultiLine_AccumulatesUntilClosingQuote()
    {
        var processor = new GameLogLineProcessor();
        var first = processor.ProcessLine("<2026-08-15T16:10:00.000Z> [Notice] <SHUDEvent_OnNotification> Added notification \"Ligne coupée en plein");
        Assert.Null(first);
        var second = processor.ProcessLine("<2026-08-15T16:10:00.000Z> milieu\" [21] to queue. New queue size: 1, MissionId: [def]");
        Assert.NotNull(second);
        Assert.Equal("Ligne coupée en plein\n milieu", second!.Text);
    }

    [Fact]
    public void HudNotification_AbandonsAfterTooManyContinuationLines()
    {
        var processor = new GameLogLineProcessor();
        processor.ProcessLine("<2026-08-15T16:10:00.000Z> [Notice] <SHUDEvent_OnNotification> Added notification \"jamais fermé");
        for (int i = 0; i < 10; i++)
        {
            var evt = processor.ProcessLine($"<2026-08-15T16:10:00.000Z> ligne {i}");
            Assert.Null(evt);
        }
        // Le motif de fermeture n'étant jamais arrivé, une ligne normale
        // doit de nouveau être traitée normalement (plus bloquée en accumulation).
        var nickname = processor.ProcessLine("<2026-08-15T16:10:00.000Z> nickname=\"Ammoniak\" more text");
        Assert.NotNull(nickname);
        Assert.Equal(GameLogEventTypes.NicknameDetected, nickname!.Type);
    }

    [Fact]
    public void HudNotification_AlternatingRepeatsWithinWindow_AreSuppressedThenResumeAfterGap()
    {
        // Vérifié en vrai Game.log : un flapping de connectivité fait alterner
        // "CommLink Restauré"/"CommLink hors service" des centaines de fois en
        // rafale — un dédoublonnage naïf sur le SEUL texte précédent ne suffit
        // pas puisque les deux textes alternent (jamais deux fois d'affilée).
        var processor = new GameLogLineProcessor();
        string Notification(string text, int id, string ts) =>
            $"<{ts}> [Notice] <SHUDEvent_OnNotification> Added notification \"{text}\" [{id}] to queue. New queue size: 1, MissionId: [x]";

        var first = processor.ProcessLine(Notification("CommLink Restauré: ", 1, "2026-09-20T18:30:31.900Z"));
        Assert.NotNull(first);

        var secondText = processor.ProcessLine(Notification("CommLink hors service: ", 2, "2026-09-20T18:30:31.910Z"));
        Assert.NotNull(secondText);

        // Rafale : mêmes deux textes qui reviennent quasi instantanément -> supprimés.
        for (var i = 0; i < 50; i++)
        {
            var restored = processor.ProcessLine(Notification("CommLink Restauré: ", 3 + i * 2, "2026-09-20T18:30:31.950Z"));
            Assert.Null(restored);
            var down = processor.ProcessLine(Notification("CommLink hors service: ", 4 + i * 2, "2026-09-20T18:30:31.950Z"));
            Assert.Null(down);
        }

        // Un vrai calme revient (> fenêtre de 30s) : la prochaine occurrence s'annonce de nouveau normalement.
        var later = processor.ProcessLine(Notification("CommLink hors service: ", 999, "2026-09-20T18:31:05.000Z"));
        Assert.NotNull(later);
        Assert.Equal("CommLink hors service:", later!.Text);
    }

    [Fact]
    public void HudNotification_GroupMemberConnectedRepeatedWithinWindow_IsSuppressedThenResumesAfterGap()
    {
        // Vérifié en vrai Game.log (remontée utilisateur, 02/10/2026) : une
        // reconnexion réseau fait apparaître DEUX notifications HUD "Groupe :
        // {nom} s'est connecté." distinctes (deux ID différents) pour le même
        // joueur à une dizaine de secondes d'écart seulement -- trop pour
        // l'ancienne fenêtre de 5s (voir le test CommLink ci-dessus), ce qui
        // laissait passer l'annonce en double à voix haute/dans l'overlay.
        var processor = new GameLogLineProcessor();
        string Notification(string text, int id, string ts) =>
            $"<{ts}> [Notice] <SHUDEvent_OnNotification> Added notification \"{text}\" [{id}] to queue. New queue size: 1, MissionId: [x]";

        var first = processor.ProcessLine(Notification("Groupe : Dionico31 s'est connecté.: ", 1, "2026-10-02T20:53:27.000Z"));
        Assert.NotNull(first);

        // 13s plus tard (vu en vrai log) : toujours dans la fenêtre de 30s -> supprimée.
        var repeated = processor.ProcessLine(Notification("Groupe : Dionico31 s'est connecté.: ", 2, "2026-10-02T20:53:40.000Z"));
        Assert.Null(repeated);

        // Un vrai calme revient (> fenêtre de 30s) : la prochaine occurrence s'annonce de nouveau normalement.
        var later = processor.ProcessLine(Notification("Groupe : Dionico31 s'est connecté.: ", 3, "2026-10-02T20:54:30.000Z"));
        Assert.NotNull(later);
    }

    [Fact]
    public void PlayerNickname_DetectedOnlyWhenNotAlreadyKnown()
    {
        var processor = new GameLogLineProcessor();
        var evt = processor.ProcessLine("<2026-08-15T16:10:00.000Z> Something nickname=\"Ammoniak\" more text");
        Assert.NotNull(evt);
        Assert.Equal("Ammoniak", evt!.Nickname);

        var processorKnown = new GameLogLineProcessor { PlayerName = "Ammoniak" };
        Assert.Null(processorKnown.ProcessLine("<2026-08-15T16:10:00.000Z> Something nickname=\"Ammoniak\" more text"));
    }

    [Fact]
    public void UnrelatedLine_EmitsNothing()
    {
        var processor = new GameLogLineProcessor();
        Assert.Null(processor.ProcessLine("<2026-08-15T16:10:00.000Z> [Notice] <Actor Init> nothing interesting here"));
    }

    // Ligne réelle vérifiée dans un vrai Game.log (30/09/2026) : émise
    // automatiquement, peu après le spawn — voir le commentaire sur
    // RequestLocationInventoryRegex pour le contexte (remplace une première
    // tentative basée sur "Legacy login response", qui ne porte en réalité
    // aucun champ Location[...] dans cette version du jeu).
    [Fact]
    public void RequestLocationInventory_EmitsZoneChangeWithoutWaitingForQuantumJump()
    {
        var processor = new GameLogLineProcessor();
        var evt = processor.ProcessLine(
            "<2026-09-30T06:38:32.439Z> [Notice] <RequestLocationInventory> Player[Ammoniak] requested " +
            "inventory for Location[RR_P6_L5] [Team_CoreGameplayFeatures][Inventory]");

        Assert.NotNull(evt);
        Assert.Equal(GameLogEventTypes.ZoneChange, evt!.Type);
        Assert.Equal("RR_P6_L5", evt.Zone);
        Assert.True(processor.State.Connected);
        Assert.Equal("RR_P6_L5", processor.State.CurrentZone);
    }

    // CORRECTIF (vrai Game.log fourni par l'utilisateur, 01/10/2026) :
    // <RequestLocationInventory> n'est pas émise qu'une seule fois au spawn
    // comme le laissait penser le constat initial ci-dessus — ouvrir un
    // inventaire de station (ATM, terminal...) sans avoir bougé entre-temps
    // la réémet aussi pour le MÊME lieu, et spammait une nouvelle annonce
    // "Arrivée à" à chaque ouverture.
    [Fact]
    public void RequestLocationInventory_RepeatedForSameLocation_DoesNotReannounce()
    {
        var processor = new GameLogLineProcessor();
        var line =
            "<2026-09-30T06:38:32.439Z> [Notice] <RequestLocationInventory> Player[Ammoniak] requested " +
            "inventory for Location[RR_P6_L5] [Team_CoreGameplayFeatures][Inventory]";

        var first = processor.ProcessLine(line);
        var second = processor.ProcessLine(line);

        Assert.NotNull(first);
        Assert.Null(second);
        Assert.Equal("RR_P6_L5", processor.State.CurrentZone);
    }

    // CORRECTIF (vrai Game.log fourni par l'utilisateur, 03/10/2026) :
    // "Arrivée à : Nyx Gateway" s'annonçait deux fois à quelques secondes
    // d'écart pour la même arrivée au point de saut Stanton-Magnus -- le
    // garde ci-dessus (RequestLocationInventory_RepeatedForSameLocation_DoesNotReannounce)
    // ne suffit pas quand les DEUX occurrences utilisent un identifiant brut
    // DIFFÉRENT pour le même lieu (ici "LOC_RS_EXT_Stan_Magnus_JP1" puis
    // "RR_JP_StantonMagnus", tous deux déjà connus du catalogue sous le même
    // nom affiché "Nyx Gateway", voir GameLogDestinations.KnownLocationAliases) :
    // State.CurrentZone ne contenait que le premier identifiant brut, jamais
    // égal au second. Comparer le nom RÉSOLU plutôt que l'identifiant brut
    // couvre ce cas.
    [Fact]
    public void RequestLocationInventory_SameResolvedZoneViaDifferentRawIdSynonym_DoesNotReannounce()
    {
        var processor = new GameLogLineProcessor();

        var first = processor.ProcessLine(
            "<2026-10-03T11:36:20.000Z> [Notice] <RequestLocationInventory> Player[Ammoniak] requested " +
            "inventory for Location[LOC_RS_EXT_Stan_Magnus_JP1] [Team_CoreGameplayFeatures][Inventory]");
        var second = processor.ProcessLine(
            "<2026-10-03T11:36:33.277Z> [Notice] <RequestLocationInventory> Player[Ammoniak] requested " +
            "inventory for Location[RR_JP_StantonMagnus] [Team_CoreGameplayFeatures][Inventory]");

        Assert.NotNull(first);
        Assert.Equal(GameLogEventTypes.ZoneChange, first!.Type);
        Assert.Null(second);

        // Un vrai retour après avoir quitté la zone reste annoncé normalement.
        processor.ProcessLine(
            "<2026-10-03T11:40:00.000Z> [Notice] <RequestLocationInventory> Player[Ammoniak] requested " +
            "inventory for Location[RR_P6_L5] [Team_CoreGameplayFeatures][Inventory]");
        var backAgain = processor.ProcessLine(
            "<2026-10-03T11:45:00.000Z> [Notice] <RequestLocationInventory> Player[Ammoniak] requested " +
            "inventory for Location[LOC_RS_EXT_Stan_Magnus_JP1] [Team_CoreGameplayFeatures][Inventory]");
        Assert.NotNull(backAgain);
    }
}
