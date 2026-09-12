using NAudio.CoreAudioApi;

namespace NovaVox.App.Speech;

/// <summary>
/// Réinitialise le volume de la session audio WASAPI de CE processus à
/// 100 % (et la démute) sur le périphérique de sortie par défaut.
/// Windows retient le volume par application dans le mixeur de volume
/// (clic droit sur le haut-parleur > Ouvrir le mixeur de volume), indexé
/// par exécutable — indépendamment de tout ce que NovaVox demande en
/// interne (DirectSoundOut.Volume ne reflète d'ailleurs jamais ce réglage
/// : sa valeur reste toujours 1.0 côté API). Si ce curseur a été mis très
/// bas lors d'un test précédent, Windows le réutilise silencieusement à
/// chaque relance, et tout ce que NovaVox joue reste inaudible même si la
/// lecture se déroule sans erreur de notre point de vue — d'où cet appel
/// défensif après chaque tentative de lecture.
/// </summary>
public static class AudioSessionVolume
{
    /// <returns>Une trace lisible de ce qui a été trouvé/changé (pour le journal système), ou null si rien n'a pu être inspecté.</returns>
    public static string? ResetToFull()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var sessions = device.AudioSessionManager.Sessions;
            var pid = (uint)Environment.ProcessId;
            var found = 0;
            string? before = null;
            for (var i = 0; i < sessions.Count; i++)
            {
                var session = sessions[i];
                if (session.GetProcessID != pid) continue;
                found++;
                before = $"{session.SimpleAudioVolume.Volume * 100:F0}% (muet={session.SimpleAudioVolume.Mute})";
                session.SimpleAudioVolume.Mute = false;
                session.SimpleAudioVolume.Volume = 1.0f;
            }
            return found == 0
                ? "Session audio Windows : aucune session trouvée pour ce processus sur le périphérique par défaut."
                : $"Session audio Windows ({device.FriendlyName}) : volume avant={before}, remis à 100% sur {found} session(s).";
        }
        catch (Exception ex)
        {
            return $"Session audio Windows : inspection impossible ({ex.GetType().Name} : {ex.Message}).";
        }
    }
}
