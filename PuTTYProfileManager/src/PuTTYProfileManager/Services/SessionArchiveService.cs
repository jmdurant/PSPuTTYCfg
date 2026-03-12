using System.IO;
using System.Text;
using System.Text.Json;
using ICSharpCode.SharpZipLib.Zip;
using Microsoft.Win32;
using PuTTYProfileManager.Models;

namespace PuTTYProfileManager.Services;

public class SessionArchiveService : ISessionArchiveService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public void ExportToZip(string zipPath, IEnumerable<PuttySession> sessions, string? password = null)
    {
        if (File.Exists(zipPath))
            File.Delete(zipPath);

        using var fs = File.Create(zipPath);
        using var zip = new ZipOutputStream(fs);

        if (!string.IsNullOrEmpty(password))
        {
            zip.Password = password;
        }

        zip.SetLevel(9);

        foreach (var session in sessions)
        {
            var dto = SessionDto.FromSession(session);
            var json = JsonSerializer.Serialize(dto, JsonOptions);
            var bytes = Encoding.UTF8.GetBytes(json);

            var entry = new ZipEntry($"{session.EncodedName}.json")
            {
                Size = bytes.Length
            };

            if (!string.IsNullOrEmpty(password))
            {
                entry.AESKeySize = 256;
            }

            zip.PutNextEntry(entry);
            zip.Write(bytes, 0, bytes.Length);
            zip.CloseEntry();
        }
    }

    public List<PuttySession> ImportFromZip(string zipPath, string? password = null)
    {
        var sessions = new List<PuttySession>();

        using var fs = File.OpenRead(zipPath);
        using var zip = new ZipInputStream(fs);

        if (!string.IsNullOrEmpty(password))
        {
            zip.Password = password;
        }

        ZipEntry? entry;
        while ((entry = zip.GetNextEntry()) is not null)
        {
            if (!entry.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                continue;

            using var ms = new MemoryStream();
            zip.CopyTo(ms);
            ms.Position = 0;

            var dto = JsonSerializer.Deserialize<SessionDto>(ms, JsonOptions);
            if (dto is null)
                continue;

            sessions.Add(dto.ToSession());
        }

        return sessions;
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

    private class SessionDto
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

    private class SettingDto
    {
        public string Name { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public object? Value { get; set; }
    }
}
