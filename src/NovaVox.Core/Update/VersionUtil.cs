using System.Text.RegularExpressions;
using static NovaVox.Core.Json.JsonHelpers;

namespace NovaVox.Core.Update;

/// <summary>
/// Numéro de version et notes de mise à jour déduits de patch_maj.txt —
/// port de _version_tuple/get_app_version/Api.get_patch_notes (app.py).
/// </summary>
public static partial class VersionUtil
{
    /// <summary>Repli utilisé uniquement si patch_maj.txt est absent ou ne contient aucune ligne "vX.Y.Z" reconnaissable.</summary>
    public const string FallbackVersion = "0.2.8";

    [GeneratedRegex(@"\d+")]
    private static partial Regex DigitsRegex();

    [GeneratedRegex(@"^v(\d+\.\d+(?:\.\d+)?)", RegexOptions.Multiline)]
    private static partial Regex VersionLineRegex();

    /// <summary>Convertit "0.1.2" en [0, 1, 2] pour une comparaison fiable (une comparaison de chaînes échouerait sur "0.9" vs "0.10").</summary>
    public static int[] VersionTuple(string? v)
    {
        var matches = DigitsRegex().Matches(v ?? "");
        return matches.Count == 0 ? new[] { 0 } : matches.Select(m => int.Parse(m.Value)).ToArray();
    }

    /// <summary>
    /// Compare deux tuples de version élément par élément (comme la
    /// comparaison de tuples Python : un tuple plus court qui est un
    /// préfixe de l'autre est considéré plus petit — PAS de complétion
    /// par des zéros).
    /// </summary>
    public static int CompareVersions(string? a, string? b)
    {
        var ta = VersionTuple(a);
        var tb = VersionTuple(b);
        int len = Math.Min(ta.Length, tb.Length);
        for (int i = 0; i < len; i++)
        {
            if (ta[i] != tb[i]) return ta[i].CompareTo(tb[i]);
        }
        return ta.Length.CompareTo(tb.Length);
    }

    public static bool IsNewer(string? remote, string? local) => CompareVersions(remote, local) > 0;

    /// <summary>Toute première ligne "vX.Y[.Z]" du fichier (par convention, la version la plus récente est en haut).</summary>
    public static string GetAppVersion(string patchNotesFilePath, string fallback = FallbackVersion)
    {
        try
        {
            var content = StripBom(File.ReadAllText(patchNotesFilePath));
            var match = VersionLineRegex().Match(content);
            if (match.Success) return match.Groups[1].Value;
        }
        catch
        {
            // Fichier absent/illisible : repli, comme côté Python.
        }
        return fallback;
    }

    public static string GetPatchNotes(string patchNotesFilePath)
    {
        try
        {
            return StripBom(File.ReadAllText(patchNotesFilePath)).Trim();
        }
        catch (FileNotFoundException)
        {
            return "Aucune note de mise à jour disponible pour le moment.";
        }
        catch
        {
            return "Impossible de lire les notes de mise à jour.";
        }
    }
}
