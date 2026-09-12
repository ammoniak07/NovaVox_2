using System.Collections.Concurrent;
using System.Text.Json;
using NAudio.Wave;
using NovaVox.Core.Audio;
using Vosk;

namespace NovaVox.App.Speech;

public sealed class SpeechRecognizedEventArgs : EventArgs
{
    public required string Text { get; init; }
    public IReadOnlyList<string> Alternatives { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Capture micro (NAudio) + reconnaissance vocale (Vosk) — port de
/// Api._listen_loop (app.py). Émet un texte reconnu par
/// <see cref="TextRecognized"/> ; la décision (commande/Gemini) revient à
/// l'appelant via <c>NovaVox.Core.Commands.VoiceCommandDispatcher</c>, pas
/// à cette classe, pour garder la capture audio indépendante de cette
/// logique.
/// </summary>
public sealed class SpeechListener : IDisposable
{
    public const int SampleRate = 16000;

    private readonly VoskModelCache _modelCache;
    private WaveInEvent? _waveIn;
    private VoskRecognizer? _recognizer;
    private BlockingCollection<byte[]>? _audioQueue;
    private Task? _processingTask;
    private CancellationTokenSource? _cts;
    private DateTime _lastLevelPush = DateTime.MinValue;

    /// <summary>Texte final reconnu (meilleure hypothèse) + hypothèses alternatives (SetMaxAlternatives).</summary>
    public event EventHandler<SpeechRecognizedEventArgs>? TextRecognized;

    /// <summary>Niveau RMS du bloc courant, poussé au plus toutes les ~0.12s (mètre de niveau des réglages).</summary>
    public event EventHandler<long>? MicLevelChanged;

    /// <summary>Une phrase d'arrêt a été reconnue PENDANT que l'appli parlait — à l'appelant d'interrompre la lecture.</summary>
    public event EventHandler? StopPhraseRecognized;

    public event EventHandler<string>? ErrorOccurred;
    public event EventHandler? Started;
    public event EventHandler? Stopped;

    // Réglages pilotés depuis l'extérieur (réglages "Sons"), relus à
    // chaque bloc audio plutôt que figés au démarrage de l'écoute.
    public string ListenMode { get; set; } = "always"; // "always" | "toggle_key" | "push_to_talk"
    public bool MicGateOpen { get; set; } = true; // piloté par le raccourci PTT/bascule (voir hotkeys)
    public int MicGate { get; set; }
    public double MicGain { get; set; } = 1.0;

    /// <summary>true pendant que l'appli lit une réponse à voix haute — anti-écho (voir _is_speaking côté Python).</summary>
    public bool IsSpeaking { get; set; }

    /// <summary>Ignore tout ce qui est reconnu jusqu'à cet instant (anti-écho, juste après une lecture).</summary>
    public DateTime SpeechMuteUntil { get; set; } = DateTime.MinValue;

    public Func<string, bool>? IsStopPhrase { get; set; }

    public SpeechListener(VoskModelCache modelCache)
    {
        _modelCache = modelCache;
    }

    /// <param name="deviceNumber">Index WaveIn NAudio, ou -1 pour le périphérique par défaut.</param>
    public void Start(string modelPath, int deviceNumber = -1)
    {
        Vosk.Vosk.SetLogLevel(-1);
        var model = _modelCache.GetOrLoad(modelPath, out _);
        _recognizer = new VoskRecognizer(model, SampleRate);
        // Demande à Vosk ses N meilleures hypothèses : un mot mal transcrit
        // dans la meilleure hypothèse n'empêche plus une commande de se
        // déclencher si une hypothèse voisine correspond exactement.
        _recognizer.SetMaxAlternatives(3);

        _audioQueue = new BlockingCollection<byte[]>();
        _cts = new CancellationTokenSource();

        _waveIn = new WaveInEvent
        {
            DeviceNumber = deviceNumber,
            WaveFormat = new WaveFormat(SampleRate, 16, 1),
            BufferMilliseconds = 500,
        };
        _waveIn.DataAvailable += OnDataAvailable;
        _waveIn.RecordingStopped += (_, e) =>
        {
            if (e.Exception is not null) ErrorOccurred?.Invoke(this, e.Exception.Message);
        };

        _processingTask = Task.Run(() => ProcessLoop(_cts.Token));
        _waveIn.StartRecording();
        Started?.Invoke(this, EventArgs.Empty);
    }

    public void Stop()
    {
        _waveIn?.StopRecording();
        _waveIn?.Dispose();
        _waveIn = null;

        _audioQueue?.CompleteAdding();
        _cts?.Cancel();
        try
        {
            _processingTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // Annulation attendue, rien à signaler.
        }

        _recognizer?.Dispose();
        _recognizer = null;
        Stopped?.Invoke(this, EventArgs.Empty);
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        var data = new byte[e.BytesRecorded];
        Array.Copy(e.Buffer, data, e.BytesRecorded);

        // TODO (annulation d'écho) : si activée, soustraire ici le signal
        // de référence loopback via NovaVox.Core.Audio.AecReferenceBuffer/
        // NlmsEchoCanceller avant de calculer le RMS — nécessite une
        // capture WASAPI loopback séparée, pas encore câblée.

        var rms = AudioProcessing.ComputeRms(data);

        var now = DateTime.UtcNow;
        if ((now - _lastLevelPush).TotalSeconds > 0.12)
        {
            _lastLevelPush = now;
            MicLevelChanged?.Invoke(this, rms);
        }

        if ((ListenMode is "push_to_talk" or "toggle_key") && !MicGateOpen)
        {
            Array.Clear(data); // micro coupé : silence complet transmis au moteur
        }
        else if (MicGate > 0 && rms < MicGate)
        {
            Array.Clear(data);
        }
        else if (MicGain != 1.0)
        {
            data = AudioProcessing.ApplyMicGain(data, MicGain);
        }

        try
        {
            _audioQueue?.Add(data);
        }
        catch (InvalidOperationException)
        {
            // File déjà fermée (arrêt en cours) : bloc ignoré sans risque.
        }
    }

    private void ProcessLoop(CancellationToken token)
    {
        if (_audioQueue is null || _recognizer is null) return;
        try
        {
            foreach (var data in _audioQueue.GetConsumingEnumerable(token))
            {
                if (!_recognizer.AcceptWaveform(data, data.Length)) continue;

                var (text, alternatives) = ParseResult(_recognizer.Result());
                var textLower = text.ToLowerInvariant();

                if (IsSpeaking || DateTime.UtcNow < SpeechMuteUntil)
                {
                    // Anti-écho : ce qui est reconnu pendant/juste après que
                    // l'appli a parlé n'est jamais traité comme commande ou
                    // question — SAUF le mot d'arrêt, toujours pris en compte
                    // pour pouvoir couper une réponse trop longue en cours.
                    if (IsSpeaking && text.Length > 0 && (IsStopPhrase?.Invoke(textLower) ?? false))
                        StopPhraseRecognized?.Invoke(this, EventArgs.Empty);
                    continue;
                }

                if (text.Length > 0)
                    TextRecognized?.Invoke(this, new SpeechRecognizedEventArgs { Text = text, Alternatives = alternatives });
            }
        }
        catch (OperationCanceledException)
        {
            // Arrêt normal (Stop()).
        }
    }

    /// <summary>
    /// Avec SetMaxAlternatives activé, Result() renvoie
    /// {"alternatives": [{"text": ..., "confidence": ...}, ...]} (déjà
    /// triées par confiance décroissante) au lieu d'un simple {"text": ...}.
    /// </summary>
    private static (string Text, List<string> Alternatives) ParseResult(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var texts = new List<string>();

        if (root.TryGetProperty("alternatives", out var altArray) && altArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var alt in altArray.EnumerateArray())
            {
                if (!alt.TryGetProperty("text", out var t)) continue;
                var s = (t.GetString() ?? "").Trim();
                if (s.Length > 0) texts.Add(s);
            }
        }
        else if (root.TryGetProperty("text", out var single))
        {
            var s = (single.GetString() ?? "").Trim();
            if (s.Length > 0) texts.Add(s);
        }

        return texts.Count == 0 ? ("", new List<string>()) : (texts[0], texts.Skip(1).ToList());
    }

    public void Dispose()
    {
        Stop();
        _audioQueue?.Dispose();
        _cts?.Dispose();
    }
}
