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
        var result = new ArchiveContents();

        using var fs = File.OpenRead(zipPath);
        using var zip = new ZipInputStream(fs);

        if (!string.IsNullOrEmpty(password))
            zip.Password = password;

        ZipEntry? entry;
        while ((entry = zip.GetNextEntry()) is not null)
        {
            using var ms = new MemoryStream();
            zip.CopyTo(ms);
            ms.Position = 0;

            if (entry.Name == "file_mapping.json")
            {
                var mapping = JsonSerializer.Deserialize<Dictionary<string, string>>(ms, JsonOptions);
                if (mapping is not null)
                    result.FileMapping = mapping;
            }
            else if (entry.Name.StartsWith("files/", StringComparison.OrdinalIgnoreCase))
            {
                result.LinkedFileEntries.Add(entry.Name);
            }
            else if (entry.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                var dto = JsonSerializer.Deserialize<SessionDto>(ms, JsonOptions);
                if (dto is not null)
                    result.Sessions.Add(dto.ToSession());
            }
        }

        return result;
    }

    public void ExtractLinkedFiles(string zipPath, string destinationFolder, string? password = null)
    {
        Directory.CreateDirectory(destinationFolder);

        using var fs = File.OpenRead(zipPath);
        using var zip = new ZipInputStream(fs);

        if (!string.IsNullOrEmpty(password))
            zip.Password = password;

        ZipEntry? entry;
        while ((entry = zip.GetNextEntry()) is not null)
        {
            if (!entry.Name.StartsWith("files/", StringComparison.OrdinalIgnoreCase))
                continue;

            var fileName = Path.GetFileName(entry.Name);
            var destPath = Path.Combine(destinationFolder, fileName);

            using var fileStream = File.Create(destPath);
            zip.CopyTo(fileStream);
        }
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
