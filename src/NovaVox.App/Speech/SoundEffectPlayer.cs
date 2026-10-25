using NAudio.Wave;

namespace NovaVox.App.Speech;

/// <summary>
/// Joue un petit son (PCM 16 bits mono) sur la même sortie audio que la voix
/// de Nova, au même volume. Sans attente : la lecture se fait en arrière-plan
/// et une erreur est simplement ignorée (un gag raté n'est pas grave).
/// </summary>
public static class SoundEffectPlayer
{
    public static void Play(short[] pcm, int sampleRate, double volume, string? outputDeviceName)
    {
        _ = Task.Run(() =>
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
                var format = new WaveFormat(sampleRate, 16, 1);
                var deviceGuid = AudioDevices.ResolveOutputDeviceGuid(outputDeviceName);
                using var stream = new RawSourceWaveStream(bytes, 0, bytes.Length, format);
                using IWavePlayer output = deviceGuid == Guid.Empty ? new WaveOutEvent() : new DirectSoundOut(deviceGuid);
                using var finished = new ManualResetEventSlim(false);
                output.PlaybackStopped += (_, _) => finished.Set();
                output.Init(stream);
                output.Play();
                finished.Wait(TimeSpan.FromSeconds(pcm.Length / (double)sampleRate + 2));
            }
            catch
            {
                // Pas de son pour ce gag : sans importance.
            }
        });
    }
}
