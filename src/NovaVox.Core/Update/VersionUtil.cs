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
    public const string FallbackVersion = "0.0.1";

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

    public sealed record PatchNoteItem(string Text, IReadOnlyList<string> Subs);
    public sealed record PatchNoteVersion(string Version, IReadOnlyList<PatchNoteItem> Items);

    [GeneratedRegex(@"^v(\d+\.\d+(?:\.\d+)?)\s*$")]
    private static partial Regex VersionOnlyLineRegex();

    [GeneratedRegex(@"^[=\-]{3,}$")]
    private static partial Regex SeparatorLineRegex();

    [GeneratedRegex(@"^(?<indent>\s*)[-*•]\s*(?<text>.*)$")]
    private static partial Regex BulletLineRegex();

    private sealed class MutableItem
    {
        public string Text = "";
        public readonly List<string> Subs = new();
    }

    /// <summary>
    /// Découpe le texte brut de patch_maj.txt en blocs par version — port
    /// de parsePatchNotes (script.js). Repère chaque ligne "vX.Y[.Z]" (même
    /// format que VersionLineRegex, qui donne le numéro de version courant)
    /// et ignore les lignes de séparation "----"/"====" ainsi que l'en-tête
    /// libre avant la première version. Une puce indentée d'au moins 3
    /// espaces (mesurés sur la ligne brute, avant nettoyage) devient une
    /// sous-puce rattachée à la puce de premier niveau précédente ; une
    /// ligne de continuation (sans tiret en tête) est rattachée à la
    /// dernière sous-puce si on est dans un bloc de sous-puces, sinon à la
    /// dernière puce de premier niveau.
    /// </summary>
    public static IReadOnlyList<PatchNoteVersion> ParsePatchNotes(string? text)
    {
        var versions = new List<(string Version, List<MutableItem> Items)>();
        List<MutableItem>? currentItems = null;
        MutableItem? lastTopItem = null;

        foreach (var rawLine in (text ?? "").Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var trimmed = line.Trim();

            var versionMatch = VersionOnlyLineRegex().Match(trimmed);
            if (versionMatch.Success)
            {
                currentItems = new List<MutableItem>();
                versions.Add((versionMatch.Groups[1].Value, currentItems));
                lastTopItem = null;
                continue;
            }
            if (currentItems is null) continue; // texte avant la première version : ignoré
            if (trimmed.Length == 0 || SeparatorLineRegex().IsMatch(trimmed)) continue;

            var bulletMatch = BulletLineRegex().Match(line);
            if (bulletMatch.Success)
            {
                var indent = bulletMatch.Groups["indent"].Value.Length;
                var itemText = bulletMatch.Groups["text"].Value;
                if (indent >= 3 && lastTopItem is not null)
                {
                    lastTopItem.Subs.Add(itemText);
                }
                else
                {
                    lastTopItem = new MutableItem { Text = itemText };
                    currentItems.Add(lastTopItem);
                }
                continue;
            }

            if (lastTopItem is not null)
            {
                if (lastTopItem.Subs.Count > 0)
                    lastTopItem.Subs[^1] += " " + trimmed;
                else
                    lastTopItem.Text += " " + trimmed;
            }
        }

        return versions
            .Select(v => new PatchNoteVersion(v.Version, v.Items.Select(i => new PatchNoteItem(i.Text, i.Subs)).ToList()))
            .ToList();
    }
}
