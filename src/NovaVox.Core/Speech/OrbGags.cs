namespace NovaVox.Core.Speech;

/// <summary>Les petits gags que la bulle joue de temps en temps quand elle s'ennuie.</summary>
public enum OrbGag { Fart, Hiccup, Sneeze, Yawn, StarCitizenLine, Snore }

/// <summary>
/// Répliques et sons (fabriqués par calcul, aucun enregistrement) des gags
/// de la bulle. Les sons sont du PCM 16 bits mono à <see cref="SampleRate"/>.
/// </summary>
public static class OrbGags
{
    public const int SampleRate = 22050;

    public static readonly string[] FartLines =
    {
        "C'est pas moi.",
        "C'était le moteur quantique.",
        "Hum… les propulseurs de manœuvre.",
        "Personne n'a rien entendu, d'accord ?",
        "Fuite de plasma. Rien de grave.",
    };

    public static readonly string[] SneezeLines =
    {
        "Pardon. Une poussière d'étoile.",
        "Saleté de poussière quantique.",
        "Je crois que je suis allergique au minerai.",
    };

    public static readonly string[] YawnLines =
    {
        "Bon… il se passe quelque chose, ou pas ?",
        "On va quelque part, aujourd'hui ?",
        "Je m'ennuie un peu, là.",
        "Réveille-moi s'il y a de l'action.",
    };

    public static readonly string[] StarCitizenLines =
    {
        "Quelqu'un a vu mon vaisseau ? Il était garé là…",
        "Je crois que je fais un trente k.",
        "J'ai encore perdu mon cargo.",
        "Tu as pensé à prendre une assurance ?",
        "Attention aux ascenseurs, ils ont faim aujourd'hui.",
        "Un jour, on aura les escadrons quarante-deux. Un jour.",
        "Si on se perd, on se retrouve au bar de Grim Hex.",
        "N'oublie pas de manger et de boire, toi aussi.",
    };

    public static readonly OrbGag[] AwakeGags = { OrbGag.Fart, OrbGag.Hiccup, OrbGag.Sneeze, OrbGag.Yawn, OrbGag.StarCitizenLine };

    /// <summary>Un gag au hasard (jamais deux fois le même de suite) ; endormie, elle ronfle.</summary>
    public static OrbGag PickGag(Random random, bool asleep, OrbGag? last)
    {
        if (asleep) return OrbGag.Snore;
        OrbGag gag;
        do gag = AwakeGags[random.Next(AwakeGags.Length)];
        while (gag == last && AwakeGags.Length > 1);
        return gag;
    }

    public static string PickLine(Random random, string[] lines) => lines[random.Next(lines.Length)];

    /// <summary>Petit pet discret : son grave et court qui « flotte », un peu de souffle, qui s'éteint.</summary>
    public static short[] Fart(Random random)
    {
        var duration = 0.45 + random.NextDouble() * 0.25;
        var count = (int)(SampleRate * duration);
        var samples = new short[count];
        double phase = 0, noise = 0, flutterPhase = random.NextDouble() * Math.PI * 2, wander = 0;
        var baseFrequency = 70 + random.NextDouble() * 30;
        for (var i = 0; i < count; i++)
        {
            var t = (double)i / SampleRate;
            wander += ((random.NextDouble() - 0.5) * 2 - wander) * 0.002;
            var frequency = baseFrequency + 45 * Math.Exp(-t * 5) + 10 * Math.Sin(2 * Math.PI * 9 * t) + 25 * wander;
            phase += 2 * Math.PI * frequency / SampleRate;
            noise += ((random.NextDouble() * 2 - 1) - noise) * 0.15;
            var flutter = 0.7 + 0.3 * Math.Sin(2 * Math.PI * 24 * t + flutterPhase);
            var attack = Math.Min(1, t / 0.015);
            var release = Math.Min(1, (duration - t) / 0.15);
            var envelope = attack * release * flutter;
            var value = (Math.Tanh(2.5 * Math.Sin(phase)) * 0.75 + noise * 0.35) * envelope * 0.35;
            samples[i] = ToPcm(value);
        }
        return samples;
    }

    /// <summary>Un ronflement : souffle grave qui monte et redescend, avec un léger « grésillement ».</summary>
    public static short[] Snore(Random random)
    {
        const double duration = 1.3;
        var count = (int)(SampleRate * duration);
        var samples = new short[count];
        double noise = 0, low = 0;
        for (var i = 0; i < count; i++)
        {
            var t = (double)i / SampleRate;
            noise += ((random.NextDouble() * 2 - 1) - noise) * 0.08;
            low += (noise - low) * 0.05;
            var swell = Math.Pow(Math.Sin(Math.PI * t / duration), 2);
            var rattle = 0.6 + 0.4 * Math.Sin(2 * Math.PI * 30 * t);
            samples[i] = ToPcm(low * 6 * swell * rattle * 0.25);
        }
        return samples;
    }

    private static short ToPcm(double value) => (short)Math.Round(Math.Clamp(value, -1, 1) * short.MaxValue);
}
