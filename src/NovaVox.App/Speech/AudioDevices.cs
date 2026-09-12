using NAudio.Wave;

namespace NovaVox.App.Speech;

/// <summary>
/// Résolution d'un périphérique audio par NOM plutôt que par index — port
/// de Api._resolve_input_device (app.py) : l'index système peut changer
/// d'un lancement à l'autre selon les périphériques branchés, alors que
/// le nom reste stable (voir AudioConfig).
/// </summary>
public static class AudioDevices
{
    public static int ResolveInputDeviceNumber(string? deviceName)
    {
        if (string.IsNullOrEmpty(deviceName)) return -1; // périphérique par défaut
        for (int i = 0; i < WaveInEvent.DeviceCount; i++)
        {
            if (WaveInEvent.GetCapabilities(i).ProductName == deviceName) return i;
        }
        return -1;
    }

    /// <summary>
    /// Noms des périphériques de sortie disponibles, dans l'ordre où
    /// DirectSoundOut les énumère — WaveOutEvent (MME) n'expose aucune
    /// énumération statique des périphériques de sortie dans NAudio.WinMM
    /// 2.2.1 (vérifié par réflexion sur l'assembly réelle), d'où l'usage de
    /// DirectSoundOut à la place (voir aussi PiperTtsEngine.PlayWavFile) :
    /// contrairement à WasapiOut en mode partagé, il ne demande pas au
    /// flux d'égaler exactement le format de mixage du périphérique.
    /// </summary>
    public static IReadOnlyList<string> ListOutputDeviceNames() =>
        DirectSoundOut.Devices.Select(d => d.Description).ToList();

    /// <summary>Résout un nom de périphérique de sortie en GUID DirectSound — Guid.Empty (périphérique par défaut) si absent/non trouvé.</summary>
    public static Guid ResolveOutputDeviceGuid(string? deviceName)
    {
        if (string.IsNullOrEmpty(deviceName)) return Guid.Empty;
        var match = DirectSoundOut.Devices.FirstOrDefault(d => d.Description == deviceName);
        return match?.Guid ?? Guid.Empty;
    }
}
