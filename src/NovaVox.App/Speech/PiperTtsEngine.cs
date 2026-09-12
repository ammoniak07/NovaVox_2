using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using NAudio.Wave;
using NovaVox.Core;
using NovaVox.Core.Audio;
using NovaVox.Core.Tts;

namespace NovaVox.App.Speech;

/// <summary>
/// Synthèse vocale via Piper (moteur neuronal local) — port de
/// Api._speak / _tts_worker / _speak_via_piper / _play_wav_file /
/// _interrupt_speech (app.py). Piper reste un processus externe isolé
/// (comme en Python) : aucune bibliothèque de synthèse embarquée dans le
/// processus NovaVox lui-même.
/// </summary>
public sealed class PiperTtsEngine : IDisposable
{
    /// <summary>Délai (secondes) pendant lequel tout ce qui est reconnu après une lecture reste ignoré (anti-écho).</summary>
    public const double SpeechMuteGraceSeconds = 0.6;

    private sealed record TtsRequest(string Text, string? PiperVoice);

    private readonly BlockingCollection<TtsRequest> _queue = new();
    private readonly Task _workerTask;
    private readonly object _stopLock = new();
    private ManualResetEventSlim? _currentPlaybackStop;

    public string PiperExePath { get; set; } = Path.Combine(NovaVoxPaths.BaseDirectory, "piper", "piper.exe");
    public string PiperVoicesDir { get; set; } = Path.Combine(NovaVoxPaths.BaseDirectory, "piper", "voices");
    public string? DefaultPiperVoice { get; set; }
    public double LengthScale { get; set; } = 1.0;
    public double NoiseScale { get; set; } = 0.667;
    public bool RadioEffectEnabled { get; set; }

    /// <summary>Volume appliqué au signal avant lecture (0.1-1.5) — indépendant du volume système, voir tts_volume côté Python.</summary>
    public double Volume { get; set; } = 1.0;

    public string? OutputDeviceName { get; set; }

    /// <summary>true pendant qu'un texte est en cours de lecture — à relier à SpeechListener.IsSpeaking pour l'anti-écho.</summary>
    public bool IsSpeaking { get; private set; }

    /// <summary>Ignorer tout ce qui est reconnu jusqu'à cet instant — à relier à SpeechListener.SpeechMuteUntil.</summary>
    public DateTime SpeechMuteUntil { get; private set; } = DateTime.MinValue;

    public event EventHandler<string>? ErrorOccurred;

    public PiperTtsEngine()
    {
        _workerTask = Task.Run(WorkerLoop);
    }

    /// <summary>
    /// Dépose un texte dans la file d'attente vocale (le rendu se fait sur
    /// le thread dédié). Sans piperVoice, utilise <see cref="DefaultPiperVoice"/>.
    /// </summary>
    public void Speak(string? text, string? piperVoice = null)
    {
        var cleaned = TtsTextSanitizer.StripMarkdownForSpeech((text ?? "").Trim());
        if (cleaned.Length == 0) return;
        _queue.Add(new TtsRequest(cleaned, piperVoice ?? DefaultPiperVoice));
    }

    /// <summary>Coupe immédiatement la lecture en cours et vide la file d'attente.</summary>
    public void Interrupt()
    {
        while (_queue.TryTake(out _)) { }
        lock (_stopLock) _currentPlaybackStop?.Set();
    }

    private void WorkerLoop()
    {
        foreach (var request in _queue.GetConsumingEnumerable())
        {
            IsSpeaking = true;
            try
            {
                SpeakViaPiper(request.Text, request.PiperVoice);
            }
            catch (Exception e)
            {
                ErrorOccurred?.Invoke(this, e.Message);
            }
            finally
            {
                IsSpeaking = false;
                SpeechMuteUntil = DateTime.UtcNow.AddSeconds(SpeechMuteGraceSeconds);
            }
        }
    }

    private void SpeakViaPiper(string text, string? voiceId)
    {
        if (string.IsNullOrEmpty(voiceId))
            throw new InvalidOperationException("Aucune voix Piper sélectionnée (voir Réglages > Moteur vocal).");
        if (!File.Exists(PiperExePath))
            throw new InvalidOperationException("Piper n'est pas installé (voir Réglages > Moteur vocal).");
        var modelPath = Path.Combine(PiperVoicesDir, $"{voiceId}.onnx");
        if (!File.Exists(modelPath))
            throw new InvalidOperationException($"Voix Piper « {voiceId} » non téléchargée.");

        var wavPath = Path.Combine(NovaVoxPaths.BaseDirectory, $"novavox_tts_{Guid.NewGuid():N}.wav");
        try
        {
            RunPiperProcess(text, modelPath, wavPath);
            if (!File.Exists(wavPath))
                throw new InvalidOperationException("La génération audio par Piper a échoué.");

            if (RadioEffectEnabled) RadioEffect.ApplyToWavFile(wavPath);

            PlayWavFile(wavPath);
        }
        finally
        {
            try
            {
                if (File.Exists(wavPath)) File.Delete(wavPath);
            }
            catch
            {
                // Best effort, comme côté Python.
            }
        }
    }

    private void RunPiperProcess(string text, string modelPath, string outputWavPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = PiperExePath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };
        psi.ArgumentList.Add("--model");
        psi.ArgumentList.Add(modelPath);
        psi.ArgumentList.Add("--output_file");
        psi.ArgumentList.Add(outputWavPath);
        psi.ArgumentList.Add("--length_scale");
        psi.ArgumentList.Add(LengthScale.ToString(CultureInfo.InvariantCulture));
        psi.ArgumentList.Add("--noise_scale");
        psi.ArgumentList.Add(NoiseScale.ToString(CultureInfo.InvariantCulture));

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Impossible de démarrer piper.exe.");

        process.StandardInput.Write(text);
        process.StandardInput.Close();

        if (!process.WaitForExit(60_000))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best effort.
            }
            throw new TimeoutException("Piper n'a pas répondu à temps.");
        }

        if (process.ExitCode != 0)
            throw new InvalidOperationException("La génération audio par Piper a échoué.");
    }

    /// <summary>
    /// Lit le .wav généré via NAudio (WaveOutEvent), interruptible via
    /// <see cref="Interrupt"/> — équivalent de Api._play_wav_file, gain
    /// appliqué directement sur le PCM (comme apply_mic_gain côté Python)
    /// plutôt que via le volume logiciel de sortie. Le périphérique de
    /// sortie par nom n'est pas encore câblé (voir AudioDevices) : lecture
    /// toujours sur le périphérique par défaut pour l'instant.
    /// </summary>
    private void PlayWavFile(string wavPath)
    {
        byte[] rawBytes;
        WaveFormat format;
        using (var reader = new WaveFileReader(wavPath))
        {
            format = reader.WaveFormat;
            rawBytes = new byte[reader.Length];
            reader.Read(rawBytes, 0, rawBytes.Length);
        }

        // Piper génère toujours du PCM 16 bits ; on n'applique le gain que
        // dans ce cas plutôt que de risquer de corrompre un format inattendu.
        if (format.BitsPerSample == 16 && Math.Abs(Volume - 1.0) > 0.0001)
            rawBytes = AudioProcessing.ApplyMicGain(rawBytes, Volume);

        using var sourceStream = new RawSourceWaveStream(rawBytes, 0, rawBytes.Length, format);
        using var output = new WaveOutEvent();
        output.Init(sourceStream);

        using var playbackFinished = new ManualResetEventSlim(false);
        using var stopRequested = new ManualResetEventSlim(false);
        lock (_stopLock) _currentPlaybackStop = stopRequested;

        output.PlaybackStopped += (_, _) => playbackFinished.Set();
        output.Play();

        WaitHandle.WaitAny(new[] { playbackFinished.WaitHandle, stopRequested.WaitHandle });
        if (stopRequested.IsSet) output.Stop();
        playbackFinished.Wait(TimeSpan.FromSeconds(2));

        lock (_stopLock)
        {
            if (_currentPlaybackStop == stopRequested) _currentPlaybackStop = null;
        }
    }

    public void Dispose()
    {
        Interrupt();
        _queue.CompleteAdding();
        try
        {
            _workerTask.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // Arrêt normal.
        }
        _queue.Dispose();
    }
}
