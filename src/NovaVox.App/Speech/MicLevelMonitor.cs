using NAudio.Wave;
using NovaVox.Core.Audio;

namespace NovaVox.App.Speech;

/// <summary>
/// Capture micro légère, indépendante de <see cref="SpeechListener"/> (qui
/// exige un modèle Vosk chargé) — juste pour le mètre de niveau en direct
/// des réglages (onglet Sons), afin de pouvoir régler le seuil de
/// sensibilité sans avoir à lancer l'écoute complète. Port de
/// start_mic_monitor/_mic_monitor_loop (app.py).
/// </summary>
public sealed class MicLevelMonitor : IDisposable
{
    private const int SampleRate = 16000;
    private WaveInEvent? _waveIn;
    private DateTime _lastPush = DateTime.MinValue;

    /// <summary>Niveau RMS du bloc courant, poussé au plus toutes les ~0.12s.</summary>
    public event EventHandler<long>? LevelChanged;
    public event EventHandler<string>? ErrorOccurred;

    public bool IsRunning => _waveIn is not null;

    public void Start(int deviceNumber = -1)
    {
        Stop();
        try
        {
            var waveIn = new WaveInEvent
            {
                DeviceNumber = deviceNumber,
                WaveFormat = new WaveFormat(SampleRate, 16, 1),
                BufferMilliseconds = 100,
            };
            waveIn.DataAvailable += OnDataAvailable;
            waveIn.RecordingStopped += (_, e) =>
            {
                if (e.Exception is not null) ErrorOccurred?.Invoke(this, e.Exception.Message);
            };
            waveIn.StartRecording();
            _waveIn = waveIn;
        }
        catch (Exception ex)
        {
            _waveIn = null;
            ErrorOccurred?.Invoke(this, ex.Message);
        }
    }

    public void Stop()
    {
        if (_waveIn is null) return;
        _waveIn.DataAvailable -= OnDataAvailable;
        _waveIn.StopRecording();
        _waveIn.Dispose();
        _waveIn = null;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        var data = new byte[e.BytesRecorded];
        Array.Copy(e.Buffer, data, e.BytesRecorded);
        var rms = AudioProcessing.ComputeRms(data);

        var now = DateTime.UtcNow;
        if ((now - _lastPush).TotalSeconds < 0.12) return;
        _lastPush = now;
        LevelChanged?.Invoke(this, rms);
    }

    public void Dispose() => Stop();
}
