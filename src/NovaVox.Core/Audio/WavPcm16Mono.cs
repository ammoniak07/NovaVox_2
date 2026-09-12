namespace NovaVox.Core.Audio;

public sealed record WavPcmData(short[] Samples, int SampleRate, int Channels, int BitsPerSample);

/// <summary>
/// Lecture/écriture minimale de fichiers .wav PCM entier (le format
/// produit par Piper) — équivalent du module standard `wave` de Python
/// pour ce qui est utilisé ici. Chunks inconnus ignorés (avance de leur
/// taille), comme le fait le module `wave`.
/// </summary>
public static class WavPcm16Mono
{
    public static WavPcmData Read(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);

        if (new string(reader.ReadChars(4)) != "RIFF")
            throw new InvalidDataException("Fichier .wav invalide (en-tête RIFF absent).");
        reader.ReadInt32(); // taille totale du chunk RIFF, non utilisée
        if (new string(reader.ReadChars(4)) != "WAVE")
            throw new InvalidDataException("Fichier .wav invalide (identifiant WAVE absent).");

        int channels = 0, sampleRate = 0, bitsPerSample = 0;
        byte[]? data = null;

        while (stream.Position <= stream.Length - 8)
        {
            var chunkId = new string(reader.ReadChars(4));
            int chunkSize = reader.ReadInt32();

            if (chunkId == "fmt ")
            {
                reader.ReadInt16(); // AudioFormat (1 = PCM)
                channels = reader.ReadInt16();
                sampleRate = reader.ReadInt32();
                reader.ReadInt32(); // ByteRate
                reader.ReadInt16(); // BlockAlign
                bitsPerSample = reader.ReadInt16();
                const int consumed = 16;
                if (chunkSize > consumed) reader.ReadBytes(chunkSize - consumed);
            }
            else if (chunkId == "data")
            {
                data = reader.ReadBytes(chunkSize);
            }
            else
            {
                reader.ReadBytes(chunkSize);
            }

            if (chunkSize % 2 == 1 && stream.Position < stream.Length) reader.ReadByte(); // octet de bourrage
        }

        if (data is null) throw new InvalidDataException("Fichier .wav invalide (chunk 'data' absent).");

        var samples = new short[data.Length / 2];
        for (int i = 0; i < samples.Length; i++)
            samples[i] = (short)(data[i * 2] | (data[i * 2 + 1] << 8));

        return new WavPcmData(samples, sampleRate, channels, bitsPerSample);
    }

    public static void Write(string path, short[] samples, int sampleRate)
    {
        const int channels = 1, bitsPerSample = 16;
        int byteRate = sampleRate * channels * bitsPerSample / 8;
        int blockAlign = channels * bitsPerSample / 8;
        int dataSize = samples.Length * 2;

        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);

        writer.Write("RIFF".ToCharArray());
        writer.Write(36 + dataSize);
        writer.Write("WAVE".ToCharArray());

        writer.Write("fmt ".ToCharArray());
        writer.Write(16);
        writer.Write((short)1); // PCM
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write((short)blockAlign);
        writer.Write((short)bitsPerSample);

        writer.Write("data".ToCharArray());
        writer.Write(dataSize);
        foreach (var s in samples) writer.Write(s);
    }
}
