using System.IO;
using System.Text;
using Microsoft.Win32;
using PuTTYProfileManager.Core.Models;

namespace PuTTYProfileManager.Core.Services;

/// <summary>
/// Reads and writes SSH sessions from/to ~/.ssh/config (OpenSSH format).
/// This is the standard session format on Linux and macOS.
/// Maps PuTTY session settings to their OpenSSH config equivalents.
/// </summary>
public class SshConfigSessionService : ISessionService
{
    private readonly string _configPath;

    // PuTTY setting name → SSH config keyword
    private static readonly Dictionary<string, string> PuttyToSshConfig = new(StringComparer.OrdinalIgnoreCase)
    {
        ["HostName"] = "HostName",
        ["PortNumber"] = "Port",
        ["UserName"] = "User",
        ["PublicKeyFile"] = "IdentityFile",
        ["AgentFwd"] = "ForwardAgent",
        ["X11Forward"] = "ForwardX11",
        ["Compression"] = "Compression",
        ["ProxyHost"] = "ProxyJump",
    };

    // SSH config keyword → PuTTY setting name (reverse)
    private static readonly Dictionary<string, string> SshConfigToPutty = new(StringComparer.OrdinalIgnoreCase)
    {
        ["HostName"] = "HostName",
        ["Port"] = "PortNumber",
        ["User"] = "UserName",
        ["IdentityFile"] = "PublicKeyFile",
        ["ForwardAgent"] = "AgentFwd",
        ["ForwardX11"] = "X11Forward",
        ["Compression"] = "Compression",
        ["ProxyJump"] = "ProxyHost",
        ["IdentitiesOnly"] = "IdentitiesOnly",
        ["ServerAliveInterval"] = "ServerAliveInterval",
        ["ServerAliveCountMax"] = "ServerAliveCountMax",
        ["StrictHostKeyChecking"] = "StrictHostKeyChecking",
        ["UserKnownHostsFile"] = "UserKnownHostsFile",
        ["LogLevel"] = "LogLevel",
    };

    public SshConfigSessionService(string? configPath = null)
    {
        _configPath = configPath
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "config");
    }

    public List<PuttySession> GetAllSessions()
    {
        var sessions = new List<PuttySession>();

        if (!File.Exists(_configPath))
            return sessions;

        PuttySession? current = null;

        foreach (var rawLine in File.ReadLines(_configPath))
        {
            var line = rawLine.Trim();

            // Skip comments and empty lines
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            var (keyword, value) = ParseConfigLine(line);
            if (keyword is null)
                continue;

            if (keyword.Equals("Host", StringComparison.OrdinalIgnoreCase))
            {
                // Skip wildcard entries like "Host *"
                if (value.Contains('*') || value.Contains('?'))
                {
                    current = null;
                    continue;
                }

                current = new PuttySession { EncodedName = Uri.EscapeDataString(value) };
                sessions.Add(current);
            }
            else if (current is not null)
            {
                var puttyName = SshConfigToPutty.GetValueOrDefault(keyword, keyword);
                var (kind, parsedValue) = ParseSshConfigValue(keyword, value);

                current.Values.Add(new RegistrySettingValue
                {
                    Name = puttyName,
                    Kind = kind,
                    Value = parsedValue
                });
            }
        }

        return sessions;
    }

    public PuttySession? GetSession(string encodedName)
    {
        return GetAllSessions()
            .FirstOrDefault(s => s.EncodedName.Equals(encodedName, StringComparison.OrdinalIgnoreCase));
    }

    public bool SessionExists(string encodedName)
    {
        return GetSession(encodedName) is not null;
    }

    public DateTime? GetSessionLastModified(string encodedName)
    {
        // SSH config is a single file — we can only return the file's mtime
        if (!File.Exists(_configPath))
            return null;

        return File.GetLastWriteTime(_configPath);
    }

    public void WriteSession(PuttySession session)
    {
        var sshDir = Path.GetDirectoryName(_configPath)!;
        Directory.CreateDirectory(sshDir);

        // Read existing config, remove any existing block for this session, append new block
        var existingLines = File.Exists(_configPath)
            ? File.ReadAllLines(_configPath).ToList()
            : [];

        var hostName = session.DisplayName;
        RemoveHostBlock(existingLines, hostName);

        // Build the new Host block
        var block = new List<string> { $"Host {hostName}" };

        foreach (var val in session.Values)
        {
            var sshKeyword = PuttyToSshConfig.GetValueOrDefault(val.Name);
            if (sshKeyword is null)
                continue;

            var sshValue = FormatSshConfigValue(sshKeyword, val);
            if (sshValue is not null)
                block.Add($"    {sshKeyword} {sshValue}");
        }

        // Add protocol if not SSH (PuTTY supports serial, telnet, etc.)
        var protocol = session.Protocol;
        if (protocol is not null && !protocol.Equals("ssh", StringComparison.OrdinalIgnoreCase))
        {
            block.Insert(1, $"    # PuTTY protocol: {protocol}");
        }

        // Ensure trailing newline before new block
        if (existingLines.Count > 0 && !string.IsNullOrWhiteSpace(existingLines[^1]))
            existingLines.Add("");

        existingLines.AddRange(block);
        existingLines.Add("");

        File.WriteAllLines(_configPath, existingLines);

        // Set proper permissions on Unix
        if (!OperatingSystem.IsWindows())
        {
            try { File.SetUnixFileMode(_configPath, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
            catch { /* best effort */ }
        }
    }

    public void DeleteSession(string encodedName)
    {
        if (!File.Exists(_configPath))
            return;

        var lines = File.ReadAllLines(_configPath).ToList();
        var hostName = Uri.UnescapeDataString(encodedName);
        RemoveHostBlock(lines, hostName);
        File.WriteAllLines(_configPath, lines);
    }

    private static void RemoveHostBlock(List<string> lines, string hostName)
    {
        int blockStart = -1;
        int blockEnd = -1;

        for (int i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.StartsWith("Host ", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("Host", StringComparison.OrdinalIgnoreCase))
            {
                var name = trimmed.Length > 5 ? trimmed[5..].Trim() : "";
                if (name.Equals(hostName, StringComparison.OrdinalIgnoreCase))
                {
                    blockStart = i;
                    // Find end of block (next Host line or EOF)
                    blockEnd = lines.Count;
                    for (int j = i + 1; j < lines.Count; j++)
                    {
                        var nextTrimmed = lines[j].Trim();
                        if (nextTrimmed.StartsWith("Host ", StringComparison.OrdinalIgnoreCase) ||
                            nextTrimmed.Equals("Host", StringComparison.OrdinalIgnoreCase))
                        {
                            blockEnd = j;
                            break;
                        }
                    }
                    break;
                }
            }
        }

        if (blockStart >= 0)
        {
            // Also remove trailing blank line if present
            while (blockEnd < lines.Count && string.IsNullOrWhiteSpace(lines[blockEnd - 1]))
                blockEnd--;

            // Actually remove up to but not including the next non-blank, or eat one trailing blank
            var removeEnd = blockEnd;
            if (removeEnd < lines.Count && string.IsNullOrWhiteSpace(lines[removeEnd]))
                removeEnd++;

            lines.RemoveRange(blockStart, removeEnd - blockStart);
        }
    }

    private static (string? keyword, string value) ParseConfigLine(string line)
    {
        // SSH config format: "Keyword value" or "Keyword=value"
        var eqIdx = line.IndexOf('=');
        var spaceIdx = line.IndexOf(' ');

        int splitIdx;
        if (eqIdx >= 0 && (spaceIdx < 0 || eqIdx < spaceIdx))
            splitIdx = eqIdx;
        else if (spaceIdx >= 0)
            splitIdx = spaceIdx;
        else
            return (null, "");

        var keyword = line[..splitIdx].Trim();
        var value = line[(splitIdx + 1)..].Trim();

        return (keyword, value);
    }

    private static (RegistryValueKind kind, object value) ParseSshConfigValue(string keyword, string raw)
    {
        // Port is numeric
        if (keyword.Equals("Port", StringComparison.OrdinalIgnoreCase) && int.TryParse(raw, out var port))
            return (RegistryValueKind.DWord, port);

        // Boolean-like values: yes/no → 1/0
        if (keyword is "ForwardAgent" or "ForwardX11" or "Compression" or "IdentitiesOnly")
        {
            var boolVal = raw.Equals("yes", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            return (RegistryValueKind.DWord, boolVal);
        }

        // Numeric values
        if (keyword is "ServerAliveInterval" or "ServerAliveCountMax" && int.TryParse(raw, out var num))
            return (RegistryValueKind.DWord, num);

        return (RegistryValueKind.String, raw);
    }

    private static string? FormatSshConfigValue(string sshKeyword, RegistrySettingValue val)
    {
        // Boolean settings: 1/0 → yes/no
        if (sshKeyword is "ForwardAgent" or "ForwardX11" or "Compression")
        {
            if (val.Value is int i)
                return i != 0 ? "yes" : null; // Skip "no" values — they're the default
            var str = val.Value?.ToString();
            if (str == "1") return "yes";
            return null;
        }

        // Port: skip default
        if (sshKeyword == "Port" && val.Value is int p && p == 22)
            return null;

        var value = val.Value?.ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
