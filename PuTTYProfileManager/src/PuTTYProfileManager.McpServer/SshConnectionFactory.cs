using System.Runtime.InteropServices;
using PuTTYProfileManager.Core.Models;
using PuTTYProfileManager.Core.Services;
using Renci.SshNet;

namespace PuTTYProfileManager.McpServer;

public static class SshConnectionFactory
{
    public static List<ISessionService> CreateSessionServices()
    {
        var services = new List<ISessionService>();

        // Platform-native PuTTY source
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            services.Add(new RegistrySessionService());
        else
            services.Add(new UnixSessionService());

        // Also read ~/.ssh/config on all platforms
        var sshConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "config");
        if (File.Exists(sshConfigPath))
            services.Add(new SshConfigSessionService(sshConfigPath));

        return services;
    }

    public static ISessionService CreateSessionService()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new RegistrySessionService();

        return new SshConfigSessionService();
    }

    public static List<PuttySession> GetAllSessionsMerged()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sessions = new List<PuttySession>();

        foreach (var service in CreateSessionServices())
        {
            foreach (var session in service.GetAllSessions())
            {
                // Deduplicate by display name — first source wins
                if (seen.Add(session.DisplayName))
                    sessions.Add(session);
            }
        }

        return sessions;
    }

    public static PuttySession? FindSession(string nameOrHost)
    {
        var sessions = GetAllSessionsMerged();
        return FindSessionInList(sessions, nameOrHost);
    }

    private static PuttySession? FindSessionInList(List<PuttySession> sessions, string nameOrHost)
    {
        // Exact match on display name (case-insensitive)
        var match = sessions.FirstOrDefault(s =>
            s.DisplayName.Equals(nameOrHost, StringComparison.OrdinalIgnoreCase));

        if (match is not null)
            return match;

        // Exact match on encoded name
        match = sessions.FirstOrDefault(s =>
            s.EncodedName.Equals(nameOrHost, StringComparison.OrdinalIgnoreCase));

        if (match is not null)
            return match;

        // Partial match on display name
        match = sessions.FirstOrDefault(s =>
            s.DisplayName.Contains(nameOrHost, StringComparison.OrdinalIgnoreCase));

        return match;
    }

    public static SshClient CreateClient(PuttySession session, string? username = null, string? password = null)
    {
        var host = session.HostName
            ?? throw new InvalidOperationException($"Session '{session.DisplayName}' has no hostname configured.");

        var port = session.Port ?? 22;

        // Get username from session or parameter
        var user = username
            ?? GetSettingValue(session, "UserName")
            ?? Environment.UserName;

        var authMethods = new List<AuthenticationMethod>();

        // Try PPK/private key first
        var keyPath = GetSettingValue(session, "PublicKeyFile");
        if (!string.IsNullOrEmpty(keyPath))
        {
            keyPath = Environment.ExpandEnvironmentVariables(keyPath);

            // PuTTY uses .ppk format — SSH.NET can read OpenSSH keys directly.
            // If it's a .ppk file, check for a converted OpenSSH key alongside it.
            var effectivePath = ResolveKeyPath(keyPath);
            if (effectivePath is not null && File.Exists(effectivePath))
            {
                var keyFile = string.IsNullOrEmpty(password)
                    ? new PrivateKeyFile(effectivePath)
                    : new PrivateKeyFile(effectivePath, password);
                authMethods.Add(new PrivateKeyAuthenticationMethod(user, keyFile));
            }
        }

        // Also try default SSH keys
        foreach (var defaultKey in GetDefaultKeyPaths())
        {
            if (File.Exists(defaultKey))
            {
                try
                {
                    var keyFile = new PrivateKeyFile(defaultKey);
                    authMethods.Add(new PrivateKeyAuthenticationMethod(user, keyFile));
                }
                catch
                {
                    // Key might need a passphrase, skip it
                }
            }
        }

        // Password auth as fallback
        if (!string.IsNullOrEmpty(password))
        {
            authMethods.Add(new PasswordAuthenticationMethod(user, password));
        }

        // Keyboard-interactive (for systems that require it)
        var kbInteractive = new KeyboardInteractiveAuthenticationMethod(user);
        if (!string.IsNullOrEmpty(password))
        {
            var pw = password;
            kbInteractive.AuthenticationPrompt += (sender, e) =>
            {
                foreach (var prompt in e.Prompts)
                    prompt.Response = pw;
            };
        }
        authMethods.Add(kbInteractive);

        if (authMethods.Count == 0)
            throw new InvalidOperationException(
                $"No authentication method available for '{session.DisplayName}'. " +
                "Provide a password or ensure an SSH key is configured.");

        var connectionInfo = new ConnectionInfo(host, port, user, authMethods.ToArray());
        return new SshClient(connectionInfo);
    }

    private static string? GetSettingValue(PuttySession session, string name)
    {
        var val = session.Values
            .FirstOrDefault(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?.Value?.ToString();

        return string.IsNullOrWhiteSpace(val) ? null : val;
    }

    private static string? ResolveKeyPath(string keyPath)
    {
        // If the key file is OpenSSH format, use it directly
        if (!keyPath.EndsWith(".ppk", StringComparison.OrdinalIgnoreCase))
            return File.Exists(keyPath) ? keyPath : null;

        // For .ppk files, look for an OpenSSH equivalent alongside it
        var dir = Path.GetDirectoryName(keyPath) ?? ".";
        var baseName = Path.GetFileNameWithoutExtension(keyPath);

        // Common patterns: key.ppk → key, key.ppk → key.pem, key.ppk → key_openssh
        string[] candidates =
        [
            Path.Combine(dir, baseName),
            Path.Combine(dir, $"{baseName}.pem"),
            Path.Combine(dir, $"{baseName}_openssh"),
            keyPath // try the .ppk itself — SSH.NET may support newer PPK formats
        ];

        return candidates.FirstOrDefault(File.Exists);
    }

    private static IEnumerable<string> GetDefaultKeyPaths()
    {
        var sshDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");

        if (!Directory.Exists(sshDir))
            yield break;

        yield return Path.Combine(sshDir, "id_ed25519");
        yield return Path.Combine(sshDir, "id_rsa");
        yield return Path.Combine(sshDir, "id_ecdsa");
    }
}
