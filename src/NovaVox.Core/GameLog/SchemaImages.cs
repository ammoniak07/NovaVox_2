using System.Text.RegularExpressions;

namespace NovaVox.Core.GameLog;

public static class SchemaImages
{
    public const string FolderName = "SchemaImages";

    private static readonly string[] Extensions = { ".jpg", ".png" };
    private static readonly Regex NonAlphanumeric = new("[^a-z0-9]+");

    public static string FileStem(string canonicalName) =>
        NonAlphanumeric.Replace(canonicalName.ToLowerInvariant(), "-").Trim('-');

    public static string? FindPath(string baseDir, string canonicalName)
    {
        var stem = Path.Combine(baseDir, FolderName, FileStem(canonicalName));
        return Extensions.Select(ext => stem + ext).FirstOrDefault(File.Exists);
    }
}
