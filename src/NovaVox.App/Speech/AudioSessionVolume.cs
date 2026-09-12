using NAudio.CoreAudioApi;

namespace NovaVox.App.Speech;

/// <summary>
/// Réinitialise le volume de la session audio WASAPI de CE processus à
/// 100 % (et la démute), quel que soit le périphérique de sortie WASAPI
/// sur lequel elle s'est réellement créée. Windows retient le volume par
/// application dans le mixeur de volume (clic droit sur le haut-parleur >
/// Ouvrir le mixeur de volume), indexé par exécutable — indépendamment de
/// tout ce que NovaVox demande en interne (DirectSoundOut.Volume ne
/// reflète d'ailleurs jamais ce réglage : sa valeur reste toujours 1.0
/// côté API). Si ce curseur a été mis très bas lors d'un test précédent,
/// Windows le réutilise silencieusement à chaque relance, et tout ce que
/// NovaVox joue reste inaudible même si la lecture se déroule sans erreur
/// de notre point de vue — d'où cet appel défensif après chaque tentative
/// de lecture.
///
/// Cherche sur TOUS les périphériques de rendu actifs, pas seulement le
/// périphérique par défaut : DirectSoundOut route l'audio vers le
/// périphérique DirectSound choisi par l'utilisateur (voir AudioDevices),
/// dont l'identifiant n'a aucun rapport avec les GUID de périphériques
/// WASAPI/MMDevice (deux systèmes d'adressage différents) — si ce
/// périphérique n'est pas celui par défaut, sa session n'apparaît jamais
/// en ne regardant que ce dernier.
/// </summary>
public static class AudioSessionVolume
{
    /// <returns>Une trace lisible de ce qui a été trouvé/changé (pour le journal système), ou null si rien n'a pu être inspecté.</returns>
    public static string? ResetToFull()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var pid = (uint)Environment.ProcessId;
            var found = 0;
            string? before = null;
            string? deviceName = null;

            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                using (device)
                {
                    var sessions = device.AudioSessionManager.Sessions;
                    for (var i = 0; i < sessions.Count; i++)
                    {
                        var session = sessions[i];
                        if (session.GetProcessID != pid) continue;
                        found++;
                        before = $"{session.SimpleAudioVolume.Volume * 100:F0}% (muet={session.SimpleAudioVolume.Mute})";
                        deviceName = device.FriendlyName;
                        session.SimpleAudioVolume.Mute = false;
                        session.SimpleAudioVolume.Volume = 1.0f;
                    }
                }
            }

            return found == 0
                ? "Session audio Windows : aucune session trouvée pour ce processus sur aucun périphérique de sortie actif."
                : $"Session audio Windows ({deviceName}) : volume avant={before}, remis à 100% sur {found} session(s).";
        }
        catch (Exception ex)
        {
            return $"Session audio Windows : inspection impossible ({ex.GetType().Name} : {ex.Message}).";
        }
    }
}
