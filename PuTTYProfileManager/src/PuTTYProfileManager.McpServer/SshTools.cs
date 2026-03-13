using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using PuTTYProfileManager.Core.Services;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace PuTTYProfileManager.McpServer;

[McpServerToolType]
public static class SshTools
{
    [McpServerTool(Name = "list_sessions"),
     Description("List all available SSH session profiles from PuTTY (Windows Registry) " +
                  "and ~/.ssh/config. Shows host, port, user, and auth method for each.")]
    public static string ListSessions()
    {
        var sessions = SshConnectionFactory.GetAllSessionsMerged();

        if (sessions.Count == 0)
            return "No SSH sessions found. Check PuTTY sessions or ~/.ssh/config.";

        var sb = new StringBuilder();
        sb.AppendLine($"Available sessions ({sessions.Count}):");
        sb.AppendLine();

        foreach (var s in sessions.OrderBy(s => s.DisplayName))
        {
            var user = s.Values
                .FirstOrDefault(v => v.Name.Equals("UserName", StringComparison.OrdinalIgnoreCase))
                ?.Value?.ToString();

            var hasKey = s.Values.Any(v =>
                v.Name.Equals("PublicKeyFile", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(v.Value?.ToString()));

            var userPart = !string.IsNullOrEmpty(user) ? $"{user}@" : "";
            var authPart = hasKey ? " [key]" : "";

            sb.AppendLine($"  {s.DisplayName,-25} {userPart}{s.Summary}{authPart}");
        }

        return sb.ToString();
    }

    [McpServerTool(Name = "ssh_execute"),
     Description("Execute a command on a remote system via SSH using a session profile. " +
                  "Reads profiles from PuTTY (Windows) and ~/.ssh/config (all platforms). " +
                  "Use list_sessions first to see available profiles. " +
                  "If authentication fails, provide a password/passphrase — it will be cached " +
                  "for the remainder of the session so you won't need to pass it again.")]
    public static string SshExecute(
        [Description("Name of the session profile to connect to")] string session,
        [Description("Shell command to execute on the remote system")] string command,
        [Description("SSH username (optional, uses session or current user if omitted)")] string? username = null,
        [Description("SSH password or key passphrase (optional, cached after first use)")] string? password = null,
        [Description("Command timeout in seconds (default: 30)")] int timeout = 30,
        [Description("Run the command with sudo (default: false)")] bool sudo = false)
    {
        var authError = ConnectWithCache(session, username, password, out var puttySession, out var client, timeout);
        if (authError is not null) return authError;

        var effectiveCommand = sudo ? $"sudo {command}" : command;

        using (client)
        try
        {
            using var cmd = client!.CreateCommand(effectiveCommand);
            cmd.CommandTimeout = TimeSpan.FromSeconds(timeout);
            var result = cmd.Execute();
            var stderr = cmd.Error;
            var exitCode = cmd.ExitStatus;

            var sb = new StringBuilder();
            sb.AppendLine($"[{puttySession!.DisplayName}] $ {effectiveCommand}");
            sb.AppendLine($"Exit code: {exitCode}");

            if (!string.IsNullOrWhiteSpace(result))
            {
                sb.AppendLine("--- stdout ---");
                sb.Append(result);
                if (!result.EndsWith('\n'))
                    sb.AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(stderr))
            {
                sb.AppendLine("--- stderr ---");
                sb.Append(stderr);
                if (!stderr.EndsWith('\n'))
                    sb.AppendLine();
            }

            return sb.ToString();
        }
        finally
        {
            client!.Disconnect();
        }
    }

    [McpServerTool(Name = "ssh_execute_multi"),
     Description("Execute multiple commands sequentially on a remote system via SSH, " +
                  "reusing a single connection. Returns output for each command. " +
                  "Credentials are cached after first use.")]
    public static string SshExecuteMulti(
        [Description("Name of the session profile to connect to")] string session,
        [Description("List of shell commands to execute in order")] string[] commands,
        [Description("SSH username (optional)")] string? username = null,
        [Description("SSH password or key passphrase (optional, cached after first use)")] string? password = null,
        [Description("Per-command timeout in seconds (default: 30)")] int timeout = 30,
        [Description("Run all commands with sudo (default: false)")] bool sudo = false)
    {
        var authError = ConnectWithCache(session, username, password, out var puttySession, out var client, timeout);
        if (authError is not null) return authError;

        using (client)
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Connected to {puttySession!.DisplayName} ({puttySession.HostName})");
            sb.AppendLine();

            foreach (var command in commands)
            {
                var effectiveCommand = sudo ? $"sudo {command}" : command;
                sb.AppendLine($"$ {effectiveCommand}");

                using var cmd = client!.CreateCommand(effectiveCommand);
                cmd.CommandTimeout = TimeSpan.FromSeconds(timeout);
                var result = cmd.Execute();
                var stderr = cmd.Error;

                if (!string.IsNullOrWhiteSpace(result))
                    sb.Append(result);
                if (!string.IsNullOrWhiteSpace(stderr))
                {
                    sb.AppendLine($"[stderr] {stderr.TrimEnd()}");
                }

                sb.AppendLine($"[exit: {cmd.ExitStatus}]");
                sb.AppendLine();
            }

            return sb.ToString();
        }
        finally
        {
            client!.Disconnect();
        }
    }

    [McpServerTool(Name = "ssh_read_file"),
     Description("Read the contents of a file on a remote system via SSH. Credentials are cached after first use.")]
    public static string SshReadFile(
        [Description("Name of the session profile")] string session,
        [Description("Absolute path to the file on the remote system")] string path,
        [Description("SSH username (optional)")] string? username = null,
        [Description("SSH password or key passphrase (optional, cached after first use)")] string? password = null)
    {
        return SshExecute(session, $"cat {EscapeArg(path)}", username, password);
    }

    [McpServerTool(Name = "ssh_write_file"),
     Description("Write content to a file on a remote system via SSH. Creates or overwrites the file. " +
                  "Credentials are cached after first use.")]
    public static string SshWriteFile(
        [Description("Name of the session profile")] string session,
        [Description("Absolute path to the file on the remote system")] string path,
        [Description("Content to write to the file")] string content,
        [Description("SSH username (optional)")] string? username = null,
        [Description("SSH password or key passphrase (optional, cached after first use)")] string? password = null,
        [Description("Write the file with sudo (default: false)")] bool sudo = false)
    {
        var authError = ConnectWithCache(session, username, password, out var puttySession, out var client);
        if (authError is not null) return authError;

        var sudoPrefix = sudo ? "sudo " : "";

        using (client)
        try
        {
            using var sftpCommand = client!.CreateCommand(
                $"{sudoPrefix}cat > {EscapeArg(path)} << 'PUTTYMGR_EOF'\n{content}\nPUTTYMGR_EOF");
            sftpCommand.CommandTimeout = TimeSpan.FromSeconds(30);
            sftpCommand.Execute();

            if (sftpCommand.ExitStatus != 0)
                return $"Error writing file: {sftpCommand.Error}";

            using var verify = client.CreateCommand($"wc -c < {EscapeArg(path)}");
            var size = verify.Execute().Trim();

            return $"Written {size} bytes to {path} on {puttySession!.DisplayName}";
        }
        finally
        {
            client!.Disconnect();
        }
    }

    [McpServerTool(Name = "ssh_upload"),
     Description("Upload a local file to a remote system via SCP. Credentials are cached after first use.")]
    public static string SshUpload(
        [Description("Name of the session profile")] string session,
        [Description("Path to the local file to upload")] string localPath,
        [Description("Destination path on the remote system")] string remotePath,
        [Description("SSH username (optional)")] string? username = null,
        [Description("SSH password or key passphrase (optional, cached after first use)")] string? password = null)
    {
        if (!File.Exists(localPath))
            throw new FileNotFoundException($"Local file not found: {localPath}", localPath);

        var authError = ConnectWithCache(session, username, password, out var puttySession, out var client);
        if (authError is not null) return authError;

        using (client)
        {
            var scpClient = new ScpClient(client!.ConnectionInfo);
            try
            {
                scpClient.Connect();

                using var fileStream = File.OpenRead(localPath);
                scpClient.Upload(fileStream, remotePath);

                var size = new FileInfo(localPath).Length;
                return $"Uploaded {localPath} ({size:N0} bytes) to {puttySession!.DisplayName}:{remotePath}";
            }
            finally
            {
                scpClient.Disconnect();
                scpClient.Dispose();
                client.Disconnect();
            }
        }
    }

    [McpServerTool(Name = "ssh_download"),
     Description("Download a file from a remote system via SCP. Credentials are cached after first use.")]
    public static string SshDownload(
        [Description("Name of the session profile")] string session,
        [Description("Path to the file on the remote system")] string remotePath,
        [Description("Local destination path")] string localPath,
        [Description("SSH username (optional)")] string? username = null,
        [Description("SSH password or key passphrase (optional, cached after first use)")] string? password = null)
    {
        var authError = ConnectWithCache(session, username, password, out var puttySession, out var client);
        if (authError is not null) return authError;

        using (client)
        {
            var scpClient = new ScpClient(client!.ConnectionInfo);
            try
            {
                scpClient.Connect();

                var dir = Path.GetDirectoryName(localPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                using var fileStream = File.Create(localPath);
                scpClient.Download(remotePath, fileStream);

                var size = new FileInfo(localPath).Length;
                return $"Downloaded {puttySession!.DisplayName}:{remotePath} ({size:N0} bytes) to {localPath}";
            }
            finally
            {
                scpClient.Disconnect();
                scpClient.Dispose();
                client.Disconnect();
            }
        }
    }

    [McpServerTool(Name = "ssh_session_info"),
     Description("Get detailed connection info for a session profile, " +
                  "including host, port, protocol, username, and authentication settings.")]
    public static string SshSessionInfo(
        [Description("Name of the session profile")] string session)
    {
        var puttySession = SshConnectionFactory.FindSession(session)
            ?? throw new InvalidOperationException(
                $"Session '{session}' not found. Use list_sessions to see available profiles.");

        var sb = new StringBuilder();
        sb.AppendLine($"Session: {puttySession.DisplayName}");
        sb.AppendLine($"  Host:     {puttySession.HostName ?? "(not set)"}");
        sb.AppendLine($"  Port:     {puttySession.Port ?? 22}");
        sb.AppendLine($"  Protocol: {puttySession.Protocol ?? "ssh"}");

        var importantSettings = new[]
        {
            "UserName", "PublicKeyFile", "DetachedCertificate",
            "ProxyHost", "ProxyPort", "ProxyMethod",
            "AgentFwd", "X11Forward", "Compression"
        };

        foreach (var name in importantSettings)
        {
            var val = puttySession.Values
                .FirstOrDefault(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (val is null)
                continue;

            var display = val.Value?.ToString();
            if (string.IsNullOrWhiteSpace(display) || display == "0")
                continue;

            sb.AppendLine($"  {name,-20} {display}");
        }

        sb.AppendLine($"  Total settings: {puttySession.Values.Count}");
        return sb.ToString();
    }

    /// <summary>
    /// Resolve a session and connect an SSH client with credential caching and auth error handling.
    /// Returns an error message string if auth fails, or null on success.
    /// </summary>
    private static string? ConnectWithCache(
        string session, string? username, string? password,
        out PuTTYProfileManager.Core.Models.PuttySession? puttySession,
        out SshClient? client,
        int timeout = 30)
    {
        puttySession = SshConnectionFactory.FindSession(session)
            ?? throw new InvalidOperationException($"Session '{session}' not found.");

        var effectivePassword = SshConnectionFactory.ResolvePassword(puttySession.DisplayName, password);

        client = SshConnectionFactory.CreateClient(puttySession, username, effectivePassword);
        client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(timeout);

        try
        {
            client.Connect();
            return null;
        }
        catch (SshAuthenticationException)
        {
            SshConnectionFactory.ClearCredential(puttySession.DisplayName);
            client.Dispose();
            client = null;
            return $"Authentication failed for session '{puttySession.DisplayName}'. " +
                   "Please call again with the password parameter. It will be cached for subsequent calls.";
        }
        catch (SshPassPhraseNullOrEmptyException)
        {
            client.Dispose();
            client = null;
            return $"The private key for session '{puttySession.DisplayName}' requires a passphrase. " +
                   "Please call again with the password parameter set to the key passphrase.";
        }
    }

    private static string EscapeArg(string arg)
    {
        return "'" + arg.Replace("'", "'\\''") + "'";
    }
}
