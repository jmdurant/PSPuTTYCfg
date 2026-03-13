using System.IO;
using System.Text;
using System.Text.Json;
using ICSharpCode.SharpZipLib.Zip;
using Microsoft.Win32;
using PuTTYProfileManager.Core.Models;

namespace PuTTYProfileManager.Core.Services;

public class SessionArchiveService : ISessionArchiveService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public void ExportToZip(string zipPath, IEnumerable<PuttySession> sessions, bool includeLinkedFiles, string? password = null)
    {
        if (File.Exists(zipPath))
            File.Delete(zipPath);

        var sessionList = sessions.ToList();
        var linkedFiles = includeLinkedFiles
            ? LinkedFileService.GetLinkedFiles(sessionList).Where(f => f.Exists).ToList()
            : [];

        var fileMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var lf in linkedFiles)
        {
            fileMapping[lf.OriginalPath] = LinkedFileService.GetZipEntryName(lf.OriginalPath);
        }

        using var fs = File.Create(zipPath);
        using var zip = new ZipOutputStream(fs);

        if (!string.IsNullOrEmpty(password))
            zip.Password = password;

        zip.SetLevel(9);

        foreach (var session in sessionList)
        {
            var dto = SessionDto.FromSession(session);
            var json = JsonSerializer.Serialize(dto, JsonOptions);
            var bytes = Encoding.UTF8.GetBytes(json);

            var entry = new ZipEntry($"sessions/{session.EncodedName}.json") { Size = bytes.Length };
            if (!string.IsNullOrEmpty(password))
                entry.AESKeySize = 256;

            zip.PutNextEntry(entry);
            zip.Write(bytes, 0, bytes.Length);
            zip.CloseEntry();
        }

        foreach (var lf in linkedFiles)
        {
            var fileBytes = File.ReadAllBytes(lf.OriginalPath);
            var entryName = fileMapping[lf.OriginalPath];

            var entry = new ZipEntry(entryName) { Size = fileBytes.Length };
            if (!string.IsNullOrEmpty(password))
                entry.AESKeySize = 256;

            zip.PutNextEntry(entry);
            zip.Write(fileBytes, 0, fileBytes.Length);
            zip.CloseEntry();
        }

        if (fileMapping.Count > 0)
        {
            var manifest = JsonSerializer.Serialize(fileMapping, JsonOptions);
            var manifestBytes = Encoding.UTF8.GetBytes(manifest);

            var entry = new ZipEntry("file_mapping.json") { Size = manifestBytes.Length };
            if (!string.IsNullOrEmpty(password))
                entry.AESKeySize = 256;

            zip.PutNextEntry(entry);
            zip.Write(manifestBytes, 0, manifestBytes.Length);
            zip.CloseEntry();
        }
    }

    public ArchiveContents ImportFromZip(string zipPath, string? password = null)
    {
        if (!File.Exists(zipPath))
            throw new FileNotFoundException($"Archive not found: {zipPath}", zipPath);

        var result = new ArchiveContents();
        var errors = new List<string>();

        using var fs = File.OpenRead(zipPath);
        using var zip = new ZipFile(fs);

        if (!string.IsNullOrEmpty(password))
            zip.Password = password;

        foreach (ZipEntry entry in zip)
        {
            if (!entry.IsFile)
                continue;

            using var stream = zip.GetInputStream(entry);
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            ms.Position = 0;

            if (entry.Name == "file_mapping.json")
            {
                try
                {
                    var mapping = JsonSerializer.Deserialize<Dictionary<string, string>>(ms, JsonOptions);
                    if (mapping is not null)
                        result.FileMapping = mapping;
                }
                catch (JsonException ex)
                {
                    errors.Add($"Invalid file_mapping.json: {ex.Message}");
                }
            }
            else if (entry.Name.StartsWith("files/", StringComparison.OrdinalIgnoreCase))
            {
                result.LinkedFileEntries.Add(entry.Name);
            }
            else if (entry.Name.StartsWith("sessions/", StringComparison.OrdinalIgnoreCase) &&
                     entry.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var dto = JsonSerializer.Deserialize<SessionDto>(ms, JsonOptions);
                    if (dto is null)
                    {
                        errors.Add($"Empty session data in {entry.Name}");
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(dto.Name))
                    {
                        errors.Add($"Session in {entry.Name} has no name");
                        continue;
                    }

                    result.Sessions.Add(dto.ToSession());
                }
                catch (JsonException ex)
                {
                    errors.Add($"Invalid session JSON in {entry.Name}: {ex.Message}");
                }
            }
        }

        result.ValidationErrors = errors;
        return result;
    }

    public void ExtractLinkedFiles(string zipPath, string destinationFolder, string? password = null)
    {
        Directory.CreateDirectory(destinationFolder);

        using var fs = File.OpenRead(zipPath);
        using var zip = new ZipFile(fs);

        if (!string.IsNullOrEmpty(password))
            zip.Password = password;

        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (ZipEntry entry in zip)
        {
            if (!entry.IsFile || !entry.Name.StartsWith("files/", StringComparison.OrdinalIgnoreCase))
                continue;

            var archiveFileName = Path.GetFileName(entry.Name);

            // Strip the hash prefix (e.g., "a1b2c3d4_key.ppk" → "key.ppk")
            // but keep the full name if stripping would cause a collision
            var friendlyName = StripHashPrefix(archiveFileName);
            var destName = usedNames.Add(friendlyName) ? friendlyName : archiveFileName;
            var destPath = Path.Combine(destinationFolder, destName);

            using var stream = zip.GetInputStream(entry);
            using var fileStream = File.Create(destPath);
            stream.CopyTo(fileStream);
        }
    }

    private static string StripHashPrefix(string fileName)
    {
        // Hash prefix format: 8 hex chars + underscore (e.g., "a1b2c3d4_")
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

    public bool IsPasswordProtected(string zipPath)
    {
        try
        {
            using var fs = File.OpenRead(zipPath);
            using var zip = new ZipFile(fs);

            foreach (ZipEntry entry in zip)
            {
                if (entry.IsCrypted)
                    return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    internal class SessionDto
    {
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public List<SettingDto> Values { get; set; } = [];

        public static SessionDto FromSession(PuttySession session) => new()
        {
            Name = session.EncodedName,
            DisplayName = session.DisplayName,
            Values = session.Values.Select(v => new SettingDto
            {
                Name = v.Name,
                Kind = v.Kind.ToString(),
                Value = ConvertValueForJson(v.Value, v.Kind)
            }).ToList()
        };

        public PuttySession ToSession()
        {
            var session = new PuttySession { EncodedName = Name };
            foreach (var dto in Values)
            {
                var kind = Enum.Parse<RegistryValueKind>(dto.Kind);
                session.Values.Add(new RegistrySettingValue
                {
                    Name = dto.Name,
                    Kind = kind,
                    Value = ConvertValueFromJson(dto.Value, kind)
                });
            }
            return session;
        }

        private static object? ConvertValueForJson(object? value, RegistryValueKind kind) => kind switch
        {
            RegistryValueKind.Binary when value is byte[] bytes => Convert.ToBase64String(bytes),
            RegistryValueKind.MultiString when value is string[] arr => string.Join("\n", arr),
            _ => value
        };

        private static object? ConvertValueFromJson(object? value, RegistryValueKind kind)
        {
            if (value is JsonElement je)
            {
                return kind switch
                {
                    RegistryValueKind.DWord => je.GetInt32(),
                    RegistryValueKind.QWord => je.GetInt64(),
                    RegistryValueKind.String or RegistryValueKind.ExpandString => je.GetString() ?? string.Empty,
                    RegistryValueKind.Binary => Convert.FromBase64String(je.GetString() ?? string.Empty),
                    RegistryValueKind.MultiString => (je.GetString() ?? string.Empty).Split('\n'),
                    _ => je.ToString()
                };
            }
            return value;
        }
    }

    internal class SettingDto
    {
        public string Name { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public object? Value { get; set; }
    }
}
