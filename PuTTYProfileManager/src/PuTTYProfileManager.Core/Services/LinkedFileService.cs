using System.IO;
using PuTTYProfileManager.Core.Models;

namespace PuTTYProfileManager.Core.Services;

public static class LinkedFileService
{
    private static readonly HashSet<string> FilePathSettings = new(StringComparer.OrdinalIgnoreCase)
    {
        "PublicKeyFile",
        "DetachedCertificate",
        "GSSCustom",
        "X11AuthFile",
        "BellWaveFile"
    };

    public static bool IsFilePathSetting(string settingName) =>
        FilePathSettings.Contains(settingName);

    public static List<LinkedFile> GetLinkedFiles(IEnumerable<PuttySession> sessions)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<LinkedFile>();

        foreach (var session in sessions)
        {
            foreach (var value in session.Values)
            {
                if (!IsFilePathSetting(value.Name))
                    continue;

                var path = value.Value?.ToString();
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                path = Environment.ExpandEnvironmentVariables(path);

                if (!seen.Add(path))
                    continue;

                var exists = File.Exists(path);
                files.Add(new LinkedFile
                {
                    OriginalPath = path,
                    SettingName = value.Name,
                    Exists = exists,
                    FileName = Path.GetFileName(path),
                    FileSize = exists ? new FileInfo(path).Length : 0
                });
            }
        }

        return files;
    }

    public static string GetZipEntryName(string originalPath)
    {
        var dir = Path.GetDirectoryName(originalPath) ?? "";
        var hash = Math.Abs(dir.GetHashCode()).ToString("x8");
        var fileName = Path.GetFileName(originalPath);
        return $"files/{hash}_{fileName}";
    }

    public static void UpdateSessionPaths(PuttySession session, string restoreFolder, Dictionary<string, string> fileMapping)
    {
        // Track which friendly names are used to detect collisions (same logic as ExtractLinkedFiles)
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resolvedNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // First pass: resolve all mapped file names
        foreach (var kvp in fileMapping)
        {
            var archiveFileName = Path.GetFileName(kvp.Value);
            var friendlyName = StripHashPrefix(archiveFileName);
            var destName = usedNames.Add(friendlyName) ? friendlyName : archiveFileName;
            resolvedNames[kvp.Key] = destName;
        }

        // Second pass: update session values
        foreach (var value in session.Values)
        {
            if (!IsFilePathSetting(value.Name))
                continue;

            var originalPath = value.Value?.ToString();
            if (string.IsNullOrWhiteSpace(originalPath))
                continue;

            var expandedPath = Environment.ExpandEnvironmentVariables(originalPath);

            if (resolvedNames.TryGetValue(expandedPath, out var destName))
            {
                var newPath = Path.Combine(restoreFolder, destName);
                value.Value = newPath;
            }
        }
    }

    private static string StripHashPrefix(string fileName)
    {
        if (fileName.Length > 9 && fileName[8] == '_' && IsHex(fileName.AsSpan(0, 8)))
            return fileName[9..];
        return fileName;
    }

    private static bool IsHex(ReadOnlySpan<char> span)
    {
        foreach (var c in span)
        {
            if (!char.IsAsciiHexDigit(c))
                return false;
        }
        return true;
    }
}
