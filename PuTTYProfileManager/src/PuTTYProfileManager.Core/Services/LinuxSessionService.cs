using System.IO;
using Microsoft.Win32;
using PuTTYProfileManager.Core.Models;

namespace PuTTYProfileManager.Core.Services;

/// <summary>
/// Reads and writes PuTTY sessions from the Linux file-based format.
/// Sessions are stored as text files in ~/.putty/sessions/ with key=value pairs.
/// </summary>
public class LinuxSessionService : ISessionService
{
    private readonly string _sessionsDir;

    public LinuxSessionService(string? sessionsDir = null)
    {
        _sessionsDir = sessionsDir
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".putty", "sessions");
    }

    public List<PuttySession> GetAllSessions()
    {
        var sessions = new List<PuttySession>();

        if (!Directory.Exists(_sessionsDir))
            return sessions;

        foreach (var filePath in Directory.GetFiles(_sessionsDir))
        {
            var session = ReadSessionFile(filePath);
            if (session is not null)
                sessions.Add(session);
        }

        return sessions;
    }

    public bool SessionExists(string encodedName)
    {
        var filePath = Path.Combine(_sessionsDir, encodedName);
        return File.Exists(filePath);
    }

    public void WriteSession(PuttySession session)
    {
        Directory.CreateDirectory(_sessionsDir);

        var filePath = Path.Combine(_sessionsDir, session.EncodedName);
        using var writer = new StreamWriter(filePath);

        foreach (var value in session.Values)
        {
            var stringValue = FormatValue(value);
            writer.WriteLine($"{value.Name}={stringValue}");
        }
    }

    public void DeleteSession(string encodedName)
    {
        var filePath = Path.Combine(_sessionsDir, encodedName);
        if (File.Exists(filePath))
            File.Delete(filePath);
    }

    private static PuttySession? ReadSessionFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        var session = new PuttySession { EncodedName = fileName };

        foreach (var line in File.ReadLines(filePath))
        {
            var eqIndex = line.IndexOf('=');
            if (eqIndex < 0)
                continue;

            var name = line[..eqIndex];
            var rawValue = line[(eqIndex + 1)..];

            var (kind, value) = ParseValue(rawValue);
            session.Values.Add(new RegistrySettingValue
            {
                Name = name,
                Kind = kind,
                Value = value
            });
        }

        return session.Values.Count > 0 ? session : null;
    }

    private static (RegistryValueKind kind, object value) ParseValue(string raw)
    {
        // PuTTY Linux format: numeric values are bare integers, strings are bare strings.
        // We detect integers by attempting to parse.
        if (int.TryParse(raw, out var intVal))
            return (RegistryValueKind.DWord, intVal);

        // Hex values (0x...)
        if (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(raw.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out var hexVal))
            return (RegistryValueKind.DWord, hexVal);

        return (RegistryValueKind.String, raw);
    }

    private static string FormatValue(RegistrySettingValue value)
    {
        return value.Kind switch
        {
            RegistryValueKind.DWord when value.Value is int i => i.ToString(),
            RegistryValueKind.DWord when value.Value is long l => l.ToString(),
            _ => value.Value?.ToString() ?? string.Empty
        };
    }
}
