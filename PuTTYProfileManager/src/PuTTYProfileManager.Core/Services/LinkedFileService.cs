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
        foreach (var value in session.Values)
        {
            if (!IsFilePathSetting(value.Name))
                continue;

            var originalPath = value.Value?.ToString();
            if (string.IsNullOrWhiteSpace(originalPath))
                continue;

            var expandedPath = Environment.ExpandEnvironmentVariables(originalPath);

            if (fileMapping.TryGetValue(expandedPath, out var zipEntryName))
            {
                var fileName = Path.GetFileName(zipEntryName);
                var newPath = Path.Combine(restoreFolder, fileName);
                value.Value = newPath;
            }
        }
    }
}
