using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using NovaVox.Core.Targeting;

namespace NovaVox.App.Targeting;

/// <summary>Résultat d'une lecture : vaisseau reconnu (ou null) et texte brut lu.</summary>
public sealed record TargetScanResult(ShipMatch? Match, IReadOnlyList<string> Lines, string? Error = null);

/// <summary>
/// Surveille en continu la zone du HUD où le jeu affiche le nom de la cible
/// (toutes les IntervalMs), et prévient quand le vaisseau ciblé change.
/// Ne lit l'écran que si Star Citizen est la fenêtre active (réglable). La
/// cible n'est « perdue » qu'après plusieurs lectures sans nom reconnu, pour
/// éviter que l'overlay clignote sur une lecture ratée.
/// </summary>
public sealed class TargetScanner : IDisposable
{
    /// <summary>Lectures consécutives sans nom reconnu avant d'effacer la cible.</summary>
    private const int MissesBeforeClear = 3;

    private readonly HudTextReader _reader = new();
    private CancellationTokenSource? _cts;
    private TargetScanConfig _config = new();
    private string? _current;
    private int _misses;

    /// <summary>Le vaisseau ciblé a changé (null = plus de cible). Appelé hors du thread de l'interface.</summary>
    public event Action<ShipMatch?>? TargetChanged;

    /// <summary>Chaque lecture (pour l'aperçu des réglages). Appelé hors du thread de l'interface.</summary>
    public event Action<TargetScanResult>? Scanned;

    public bool OcrAvailable => _reader.IsAvailable;
    public string? OcrLanguage => _reader.LanguageTag;
    public bool IsRunning => _cts is not null;

    public void Apply(TargetScanConfig config)
    {
        _config = config;
        if (config.Enabled && config.HasZone && _reader.IsAvailable) Start();
        else Stop();
    }

    private void Start()
    {
        if (_cts is not null) return;
        var cts = new CancellationTokenSource();
        _cts = cts;
        _ = Task.Run(() => LoopAsync(cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
        SetCurrent(null);
    }

    private async Task LoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (!_config.OnlyWhenGameFocused || IsGameFocused())
                {
                    var result = await ScanOnceAsync(token);
                    if (result.Match is not null)
                    {
                        _misses = 0;
                        SetCurrent(result.Match);
                    }
                    else if (++_misses >= MissesBeforeClear)
                    {
                        SetCurrent(null);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Une lecture ratée (écran verrouillé, changement de résolution...) n'arrête pas la surveillance.
            }
            try
            {
                await Task.Delay(Math.Clamp(_config.IntervalMs, TargetScanConfig.MinIntervalMs, TargetScanConfig.MaxIntervalMs), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Une lecture de la zone réglée, sans toucher à la cible en cours (sert aussi au bouton « Tester »).</summary>
    public async Task<TargetScanResult> ScanOnceAsync(CancellationToken token = default)
    {
        if (!_reader.IsAvailable)
            return new TargetScanResult(null, Array.Empty<string>(), "Aucune langue de reconnaissance de texte n'est installée dans Windows.");
        if (!_config.HasZone)
            return new TargetScanResult(null, Array.Empty<string>(), "Zone du nom pas encore définie.");
        var lines = await _reader.ReadAsync(new Rectangle(_config.Left, _config.Top, _config.Width, _config.Height), token);
        var result = new TargetScanResult(ShipNameMatcher.MatchTargetPanel(lines), lines);
        Scanned?.Invoke(result);
        return result;
    }

    private void SetCurrent(ShipMatch? match)
    {
        var name = match?.Ship.Name;
        if (string.Equals(name, _current, StringComparison.Ordinal)) return;
        _current = name;
        TargetChanged?.Invoke(match);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    /// <summary>Star Citizen (StarCitizen.exe) est-il la fenêtre active ?</summary>
    public static bool IsGameFocused()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;
        GetWindowThreadProcessId(hwnd, out var pid);
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName.StartsWith("StarCitizen", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        Stop();
        _reader.Dispose();
    }
}
