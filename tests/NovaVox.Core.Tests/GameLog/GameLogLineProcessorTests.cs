using NovaVox.Core.GameLog;
using Xunit;

namespace NovaVox.Core.Tests.GameLog;

public class GameLogLineProcessorTests
{
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

        // Un vrai calme revient (> fenêtre de 5s) : la prochaine occurrence s'annonce de nouveau normalement.
        var later = processor.ProcessLine(Notification("CommLink hors service: ", 999, "2026-09-20T18:30:40.000Z"));
        Assert.NotNull(later);
        Assert.Equal("CommLink hors service:", later!.Text);
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
}
