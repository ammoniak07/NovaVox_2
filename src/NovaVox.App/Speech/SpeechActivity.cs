namespace NovaVox.App.Speech;

/// <summary>
/// Relais commun "NovaVox parle" / "Gemini réfléchit", quel que soit le
/// moteur qui parle (commandes, annonces, réponses Gemini, bouton Tester)
/// — écouté par la bulle animée (Overlay/OrbWindow). Les événements
/// partent du fil de lecture audio ou de la requête réseau : l'abonné doit
/// repasser lui-même sur le fil de l'interface.
/// </summary>
public static class SpeechActivity
{
    /// <summary>Une lecture vocale démarre : enveloppe sonore du clip (niveaux 0..1, un par <see cref="SpeakingStartedEventArgs.FrameMs"/>).</summary>
    public static event EventHandler<SpeakingStartedEventArgs>? SpeakingStarted;

    public static event EventHandler? SpeakingStopped;

    /// <summary>true pendant qu'une question est envoyée à Gemini, false une fois la réponse (ou l'erreur) reçue.</summary>
    public static event EventHandler<bool>? ThinkingChanged;

    private static int _thinkingCount;

    internal static void RaiseSpeakingStarted(float[] envelope, int frameMs) =>
        SpeakingStarted?.Invoke(null, new SpeakingStartedEventArgs(envelope, frameMs));

    internal static void RaiseSpeakingStopped() => SpeakingStopped?.Invoke(null, EventArgs.Empty);

    /// <summary>Compte les requêtes en cours (deux questions peuvent se chevaucher).</summary>
    internal static void BeginThinking()
    {
        if (Interlocked.Increment(ref _thinkingCount) == 1) ThinkingChanged?.Invoke(null, true);
    }

    internal static void EndThinking()
    {
        var left = Interlocked.Decrement(ref _thinkingCount);
        if (left <= 0)
        {
            Interlocked.Exchange(ref _thinkingCount, 0);
            ThinkingChanged?.Invoke(null, false);
        }
    }
}

public sealed record SpeakingStartedEventArgs(float[] Envelope, int FrameMs);
