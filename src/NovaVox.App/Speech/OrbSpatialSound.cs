using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NovaVox.Core.Audio;

namespace NovaVox.App.Speech;

/// <summary>
/// Position sonore de la bulle, mise à jour à chaque image par OrbWindow
/// et lue en continu pendant la lecture d'un son : la voix de Nova et les
/// sons des gags suivent la bulle (gauche/droite, distance) même si elle
/// bouge pendant qu'elle parle.
/// </summary>
public static class OrbSpatialSound
{
    private static double _pan, _gain = 1, _lowPass = 1;
    private static volatile bool _enabled;

    /// <summary>Vrai quand la bulle est affichée et que l'option « son spatialisé » est active.</summary>
    public static bool Enabled => _enabled;

    public static double Pan => Volatile.Read(ref _pan);
    public static double Gain => Volatile.Read(ref _gain);
    public static double LowPass => Volatile.Read(ref _lowPass);

    public static void Update(bool enabled, double pan, double gain, double lowPass)
    {
        Volatile.Write(ref _pan, pan);
        Volatile.Write(ref _gain, gain);
        Volatile.Write(ref _lowPass, lowPass);
        _enabled = enabled;
    }

    /// <summary>Source mono ou stéréo -> sortie stéréo 16 bits placée comme la bulle.</summary>
    public static IWaveProvider Wrap(ISampleProvider source)
    {
        if (source.WaveFormat.Channels == 2) source = new StereoToMonoSampleProvider(source);
        return new SampleToWaveProvider16(new SpatialSampleProvider(source));
    }

    /// <summary>Mono -> stéréo : panoramique, volume et étouffement lus en direct (et lissés pour éviter les clics).</summary>
    private sealed class SpatialSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private float[] _buffer = Array.Empty<float>();
        private double _left = -1, _right = -1, _gain = -1, _alpha = 1, _filtered;

        public SpatialSampleProvider(ISampleProvider mono)
        {
            _source = mono;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(mono.WaveFormat.SampleRate, 2);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            var frames = count / 2;
            if (_buffer.Length < frames) _buffer = new float[frames];
            var read = _source.Read(_buffer, 0, frames);

            var (targetLeft, targetRight) = SpatialAudio.PanGains(OrbSpatialSound.Enabled ? OrbSpatialSound.Pan : 0);
            var targetGain = OrbSpatialSound.Enabled ? OrbSpatialSound.Gain : 1;
            var targetAlpha = OrbSpatialSound.Enabled ? OrbSpatialSound.LowPass : 1;
            if (_gain < 0) { _left = targetLeft; _right = targetRight; _gain = targetGain; _alpha = targetAlpha; }

            for (var i = 0; i < read; i++)
            {
                // Glissement doux vers la position actuelle (pas de saut audible).
                _left += (targetLeft - _left) * 0.002;
                _right += (targetRight - _right) * 0.002;
                _gain += (targetGain - _gain) * 0.002;
                _alpha += (targetAlpha - _alpha) * 0.002;
                _filtered += (_buffer[i] - _filtered) * _alpha;
                var sample = _filtered * _gain;
                buffer[offset + 2 * i] = (float)(sample * _left);
                buffer[offset + 2 * i + 1] = (float)(sample * _right);
            }
            return read * 2;
        }
    }
}
