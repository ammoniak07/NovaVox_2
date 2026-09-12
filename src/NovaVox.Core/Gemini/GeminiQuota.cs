using NovaVox.Core.Config;

namespace NovaVox.Core.Gemini;

/// <summary>
/// Port de Api._gemini_quota_state/_gemini_record_request (app.py) : le
/// quota RPD (requêtes par jour) de Google se réinitialise à minuit heure
/// du Pacifique, pas sur une fenêtre glissante de 24h. .NET résout
/// "America/Los_Angeles" nativement sur Windows comme sur Linux (ICU/
/// tzdata), contrairement à Python où zoneinfo peut manquer de données —
/// le repli sur UTC est conservé par prudence.
/// </summary>
public static class GeminiQuota
{
    private static readonly TimeZoneInfo PacificTimeZone = ResolvePacificTimeZone();

    private static TimeZoneInfo ResolvePacificTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
        }
        catch (TimeZoneNotFoundException)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time"); // identifiant Windows
            }
            catch
            {
                return TimeZoneInfo.Utc;
            }
        }
    }

    public static string TodayIso() =>
        TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, PacificTimeZone).Date.ToString("yyyy-MM-dd");

    /// <summary>
    /// (requêtes utilisées, limite) pour le modèle courant, en remettant
    /// d'abord le compteur à zéro (mutation de <paramref name="config"/>)
    /// si on a changé de jour côté Google depuis la dernière requête.
    /// </summary>
    public static (int Used, int Limit) SyncState(AiConfig config)
    {
        var today = TodayIso();
        if (config.GeminiRequestDay != today)
        {
            config.GeminiRequestDay = today;
            config.GeminiRequestCount = 0;
        }
        return (config.GeminiRequestCount, GeminiModels.DailyLimitFor(config.GeminiModel));
    }

    /// <summary>Incrémente le compteur local (à appeler juste avant chaque appel réel à l'API).</summary>
    public static (int Used, int Limit) RecordRequest(AiConfig config)
    {
        var (used, limit) = SyncState(config);
        config.GeminiRequestCount = used + 1;
        return (config.GeminiRequestCount, limit);
    }
}
