using System.Runtime.InteropServices;
using PuTTYProfileManager.Core.Models;
using PuTTYProfileManager.Core.Services;
using Renci.SshNet;
using SshNet.PuttyKeyFile;

namespace PuTTYProfileManager.McpServer;

public static class SshConnectionFactory
{
    // In-process credential cache — persists for the lifetime of the MCP server process.
    // Keyed by session display name (case-insensitive).
    private static readonly Dictionary<string, string> _credentialCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Store a password/passphrase for a session so subsequent calls don't need it again.
    /// </summary>
    public static void CacheCredential(string sessionName, string password)
    {
        _credentialCache[sessionName] = password;
    }

    /// <summary>
    /// Get a cached credential for a session, or null if none is stored.
    /// </summary>
    public static string? GetCachedCredential(string sessionName)
    {
        return _credentialCache.TryGetValue(sessionName, out var pw) ? pw : null;
    }

    /// <summary>
    /// Remove a cached credential for a session.
    /// </summary>
    public static void ClearCredential(string sessionName)
    {
        _credentialCache.Remove(sessionName);
    }

    /// <summary>
    /// Resolve the effective password for a session: explicit parameter wins, then cache.
    /// </summary>
    public static string? ResolvePassword(string sessionName, string? explicitPassword)
    {
        if (!string.IsNullOrEmpty(explicitPassword))
        {
            CacheCredential(sessionName, explicitPassword);
            return explicitPassword;
        }

        return GetCachedCredential(sessionName);
    }

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
        var host = session.HostName?.Trim()
            ?? throw new InvalidOperationException($"Session '{session.DisplayName}' has no hostname configured.");

        if (string.IsNullOrEmpty(host))
            throw new InvalidOperationException($"Session '{session.DisplayName}' has no hostname configured.");

        var port = session.Port ?? 22;

        // Get username from session or parameter
        var user = (username
            ?? GetSettingValue(session, "UserName")
            ?? Environment.UserName).Trim();

        var authMethods = new List<AuthenticationMethod>();

        // Try PPK/private key first
        var keyPath = GetSettingValue(session, "PublicKeyFile");
        if (!string.IsNullOrEmpty(keyPath))
        {
            keyPath = Environment.ExpandEnvironmentVariables(keyPath);

            var effectivePath = ResolveKeyPath(keyPath);
            if (effectivePath is not null && File.Exists(effectivePath))
            {
                authMethods.Add(CreateKeyAuth(user, effectivePath, password));
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
        var client = new SshClient(connectionInfo);

        // Auto-accept host keys so first-time connections don't fail silently.
        // SSH.NET rejects unknown hosts by default when no handler is attached.
        client.HostKeyReceived += (sender, e) =>
        {
            e.CanTrust = true;
        };

        return client;
    }

    private static string? GetSettingValue(PuttySession session, string name)
    {
        var val = session.Values
            .FirstOrDefault(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?.Value?.ToString();

        return string.IsNullOrWhiteSpace(val) ? null : val.Trim();
    }

    /// <summary>
    /// Create a private key auth method, using PuttyKeyFile for .ppk format and PrivateKeyFile for OpenSSH format.
    /// </summary>
    private static PrivateKeyAuthenticationMethod CreateKeyAuth(string user, string path, string? password)
    {
        if (path.EndsWith(".ppk", StringComparison.OrdinalIgnoreCase))
        {
            var key = string.IsNullOrEmpty(password)
                ? new PuttyKeyFile(path)
                : new PuttyKeyFile(path, password);
            return new PrivateKeyAuthenticationMethod(user, key);
        }

        var keyFile = string.IsNullOrEmpty(password)
            ? new PrivateKeyFile(path)
            : new PrivateKeyFile(path, password);
        return new PrivateKeyAuthenticationMethod(user, keyFile);
    }

    private static string? ResolveKeyPath(string keyPath)
    {
        // Use the key file directly if it exists (works for both .ppk and OpenSSH formats now)
        if (File.Exists(keyPath))
            return keyPath;

        // If it's a .ppk path that doesn't exist, check for OpenSSH equivalents
        if (keyPath.EndsWith(".ppk", StringComparison.OrdinalIgnoreCase))
        {
            var dir = Path.GetDirectoryName(keyPath) ?? ".";
            var baseName = Path.GetFileNameWithoutExtension(keyPath);

            string[] candidates =
            [
                Path.Combine(dir, baseName),
                Path.Combine(dir, $"{baseName}.pem"),
                Path.Combine(dir, $"{baseName}_openssh"),
            ];

            return candidates.FirstOrDefault(File.Exists);
        }

        return null;
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
