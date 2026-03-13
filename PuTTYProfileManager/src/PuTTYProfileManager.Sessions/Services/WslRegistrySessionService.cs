using System.Diagnostics;
using Microsoft.Win32;
using PuTTYProfileManager.Core.Models;

namespace PuTTYProfileManager.Core.Services;

/// <summary>
/// Reads PuTTY sessions from the Windows Registry via reg.exe when running under WSL.
/// Uses a single recursive query to fetch all sessions in one call.
/// </summary>
public class WslRegistrySessionService : ISessionService
{
    private const string PuttySessionsPath = @"HKCU\SOFTWARE\SimonTatham\PuTTY\Sessions";

    public WslRegistrySessionService()
    {
        if (!WslEnvironment.IsWsl)
            throw new PlatformNotSupportedException("WslRegistrySessionService is only available under WSL.");
    }

    public List<PuttySession> GetAllSessions()
    {
        // Single recursive query to get all sessions and values
        var (exitCode, output, _) = RunRegExe($"query \"{PuttySessionsPath}\" /s");
        if (exitCode != 0 || string.IsNullOrWhiteSpace(output))
            return [];

        return ParseAllSessions(output);
    }

    public PuttySession? GetSession(string encodedName)
    {
        var (exitCode, output, _) = RunRegExe($"query \"{PuttySessionsPath}\\{encodedName}\"");
        if (exitCode != 0 || string.IsNullOrWhiteSpace(output))
            return null;

        var session = new PuttySession { EncodedName = encodedName };
        ParseValues(output, session);
        return session;
    }

    public bool SessionExists(string encodedName)
    {
        var (exitCode, _, _) = RunRegExe($"query \"{PuttySessionsPath}\\{encodedName}\"");
        return exitCode == 0;
    }

    public DateTime? GetSessionLastModified(string encodedName) => null;

    public void WriteSession(PuttySession session)
    {
        var keyPath = $"{PuttySessionsPath}\\{session.EncodedName}";

        foreach (var val in session.Values)
        {
            var regType = val.Kind switch
            {
                RegistryValueKind.DWord => "REG_DWORD",
                _ => "REG_SZ"
            };

            var regValue = val.Kind == RegistryValueKind.DWord
                ? Convert.ToInt32(val.Value ?? 0).ToString()
                : val.Value?.ToString() ?? "";

            RunRegExe($"add \"{keyPath}\" /v \"{val.Name}\" /t {regType} /d \"{regValue}\" /f");
        }
    }

    public void DeleteSession(string encodedName)
    {
        RunRegExe($"delete \"{PuttySessionsPath}\\{encodedName}\" /f");
    }

    internal static List<PuttySession> ParseAllSessions(string regOutput)
    {
        var sessions = new List<PuttySession>();
        PuttySession? current = null;

        foreach (var rawLine in regOutput.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            if (string.IsNullOrWhiteSpace(line))
                continue;

            // Lines starting with HKEY are key headers
            if (line.StartsWith("HKEY", StringComparison.OrdinalIgnoreCase))
            {
                // Extract session name from the key path
                var prefix = PuttySessionsPath.Replace("HKCU", "HKEY_CURRENT_USER") + "\\";
                if (line.Contains(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    var idx = line.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
                    var encodedName = line[(idx + prefix.Length)..].Trim();

                    if (!string.IsNullOrEmpty(encodedName) && !encodedName.Contains('\\'))
                    {
                        current = new PuttySession { EncodedName = encodedName };
                        sessions.Add(current);
                    }
                    else
                    {
                        current = null; // This is the parent key itself
                    }
                }
                continue;
            }

            // Value lines: "    Name    REG_TYPE    Value"
            if (current is not null && line.StartsWith("    "))
            {
                var value = ParseValueLine(line);
                if (value is not null)
                    current.Values.Add(value);
            }
        }

        return sessions;
    }

    internal static RegistrySettingValue? ParseValueLine(string line)
    {
        // Format: "    Name    REG_TYPE    Value"
        // Split on runs of whitespace, but be careful — value might contain spaces
        var trimmed = line.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;

        // Find REG_SZ or REG_DWORD in the line to split on it
        var regSzIdx = trimmed.IndexOf("REG_SZ", StringComparison.Ordinal);
        var regDwordIdx = trimmed.IndexOf("REG_DWORD", StringComparison.Ordinal);
        var regExpandSzIdx = trimmed.IndexOf("REG_EXPAND_SZ", StringComparison.Ordinal);

        string name;
        RegistryValueKind kind;
        string rawValue;

        if (regDwordIdx > 0)
        {
            name = trimmed[..regDwordIdx].TrimEnd();
            kind = RegistryValueKind.DWord;
            rawValue = trimmed[(regDwordIdx + "REG_DWORD".Length)..].TrimStart();
        }
        else if (regExpandSzIdx > 0)
        {
            name = trimmed[..regExpandSzIdx].TrimEnd();
            kind = RegistryValueKind.String;
            rawValue = trimmed[(regExpandSzIdx + "REG_EXPAND_SZ".Length)..].TrimStart();
        }
        else if (regSzIdx > 0)
        {
            name = trimmed[..regSzIdx].TrimEnd();
            kind = RegistryValueKind.String;
            rawValue = trimmed[(regSzIdx + "REG_SZ".Length)..].TrimStart();
        }
        else
        {
            return null;
        }

        object? value = kind == RegistryValueKind.DWord
            ? ParseDwordValue(rawValue)
            : rawValue;

        return new RegistrySettingValue
        {
            Name = name,
            Kind = kind,
            Value = value
        };
    }

    private static int ParseDwordValue(string hex)
    {
        // reg.exe outputs DWORD as "0x16" (hex)
        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return Convert.ToInt32(hex, 16);

        return int.TryParse(hex, out var result) ? result : 0;
    }

    private static void ParseValues(string regOutput, PuttySession session)
    {
        foreach (var rawLine in regOutput.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            if (line.StartsWith("    "))
            {
                var value = ParseValueLine(line);
                if (value is not null)
                    session.Values.Add(value);
            }
        }
    }

    private static (int exitCode, string stdout, string stderr) RunRegExe(string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = WslEnvironment.RegExePath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null)
                return (-1, "", "Failed to start reg.exe");

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(10000);

            return (process.ExitCode, stdout, stderr);
        }
        catch (Exception ex)
        {
            return (-1, "", ex.Message);
        }
    }
}
