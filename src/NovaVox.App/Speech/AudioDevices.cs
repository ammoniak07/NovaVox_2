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

    // NOTE : contrairement à l'entrée (WaveInEvent.DeviceCount/
    // GetCapabilities, vérifiés ci-dessus), NAudio.WinMM 2.2.1 n'expose
    // aucune énumération statique des périphériques de SORTIE pour
    // WaveOutEvent (pas de classe WaveOut avec DeviceCount/GetCapabilities
    // dans ce paquet — vérifié par réflexion sur l'assembly réelle). La
    // sélection d'un périphérique de sortie par nom (équivalent de
    // Api._resolve_output_device) nécessiterait soit l'énumération WASAPI
    // (NAudio.CoreAudioApi.MMDeviceEnumerator, ordre non garanti
    // correspondre aux index MME de WaveOutEvent), soit de passer par
    // DirectSoundOut à la place. Laissé en TODO pour la tâche UI/réglages
    // audio : la lecture se fait pour l'instant toujours sur le
    // périphérique de sortie PAR DÉFAUT du système.
}
