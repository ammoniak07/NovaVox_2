using NAudio.Wave;

namespace NovaVox.App.Speech;

/// <summary>
/// Joue un petit son sur la même sortie audio que la voix de Nova, au même
/// volume, en arrière-plan. Une erreur est simplement ignorée (un gag sans
/// son n'est pas grave). La tâche renvoyée se termine à la fin du son.
/// </summary>
public static class SoundEffectPlayer
{
    /// <summary>Son fabriqué (PCM 16 bits mono).</summary>
    public static Task Play(short[] pcm, int sampleRate, double volume, string? outputDeviceName) => Task.Run(() =>
    {
        try
        {
            var bytes = new byte[pcm.Length * 2];
            var gain = Math.Clamp(volume, 0, 2);
            for (var i = 0; i < pcm.Length; i++)
            {
                var value = (short)Math.Clamp(pcm[i] * gain, short.MinValue, short.MaxValue);
                bytes[2 * i] = (byte)(value & 0xff);
                bytes[2 * i + 1] = (byte)((value >> 8) & 0xff);
            }
            using var stream = new RawSourceWaveStream(bytes, 0, bytes.Length, new WaveFormat(sampleRate, 16, 1));
            PlayToEnd(stream, TimeSpan.FromSeconds(pcm.Length / (double)sampleRate), outputDeviceName);
        }
        catch
        {
            // Pas de son pour ce gag : sans importance.
        }
    });

    /// <summary>Fichier WAV ou MP3 (sons personnels du dossier sons_gags).</summary>
    public static Task PlayFile(string path, double volume, string? outputDeviceName) => Task.Run(() =>
    {
        try
        {
            using var reader = new AudioFileReader(path) { Volume = (float)Math.Clamp(volume, 0, 2) };
            PlayToEnd(reader, reader.TotalTime, outputDeviceName);
        }
        catch
        {
            // Fichier illisible : on ignore.
        }
    });

    private static void PlayToEnd(IWaveProvider source, TimeSpan duration, string? outputDeviceName)
    {
        var deviceGuid = AudioDevices.ResolveOutputDeviceGuid(outputDeviceName);
        using IWavePlayer output = deviceGuid == Guid.Empty ? new WaveOutEvent() : new DirectSoundOut(deviceGuid);
        using var finished = new ManualResetEventSlim(false);
        output.PlaybackStopped += (_, _) => finished.Set();
        output.Init(source);
        output.Play();
        // Au plus 10 s : un son de gag trop long est coupé.
        finished.Wait(TimeSpan.FromSeconds(Math.Min(10, duration.TotalSeconds + 2)));
    }
}
