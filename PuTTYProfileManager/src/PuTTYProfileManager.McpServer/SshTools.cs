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
                  "Connections are pooled and reused across calls to avoid per-command handshake cost. " +
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
        if (TryAcquire(session, username, password, timeout, out var lease, out var authError))
        {
            using (lease)
            {
                var effectiveCommand = sudo ? $"sudo {command}" : command;
                using var cmd = lease.Client.CreateCommand(effectiveCommand);
                cmd.CommandTimeout = TimeSpan.FromSeconds(timeout);
                var result = cmd.Execute();
                var stderr = cmd.Error;
                var exitCode = cmd.ExitStatus;

                var sb = new StringBuilder();
                sb.AppendLine($"[{lease.Session.DisplayName}] $ {effectiveCommand}");
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

                if (!sudo && exitCode != 0 && IsPermissionError(stderr))
                    sb.AppendLine("Tip: this may be a permissions issue — retry with the sudo parameter set to true.");

                return sb.ToString();
            }
        }

        return authError!;
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
        if (!TryAcquire(session, username, password, timeout, out var lease, out var authError))
            return authError!;

        using (lease)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Connected to {lease.Session.DisplayName} ({lease.Session.HostName})");
            sb.AppendLine();

            foreach (var command in commands)
            {
                var effectiveCommand = sudo ? $"sudo {command}" : command;
                sb.AppendLine($"$ {effectiveCommand}");

                using var cmd = lease.Client.CreateCommand(effectiveCommand);
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

                if (!sudo && cmd.ExitStatus != 0 && IsPermissionError(stderr))
                    sb.AppendLine("Tip: this may be a permissions issue — retry with the sudo parameter set to true.");

                sb.AppendLine();
            }

            return sb.ToString();
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
        [Description("Write the file with sudo (default: false)")] bool sudo = false,
        [Description("Overwrite if the file already exists (default: false)")] bool overwrite = false)
    {
        if (!TryAcquire(session, username, password, 30, out var lease, out var authError))
            return authError!;

        using (lease)
        {
            var sudoPrefix = sudo ? "sudo " : "";

            if (!overwrite)
            {
                using var check = lease.Client.CreateCommand($"{sudoPrefix}test -e {EscapeArg(path)} && echo EXISTS");
                var checkResult = check.Execute().Trim();
                if (checkResult == "EXISTS")
                    return $"File already exists: {path} on {lease.Session.DisplayName}. Set overwrite to true to replace it.";
            }

            using var sftpCommand = lease.Client.CreateCommand(
                $"{sudoPrefix}cat > {EscapeArg(path)} << 'PUTTYMGR_EOF'\n{content}\nPUTTYMGR_EOF");
            sftpCommand.CommandTimeout = TimeSpan.FromSeconds(30);
            sftpCommand.Execute();

            if (sftpCommand.ExitStatus != 0)
            {
                var errorMsg = $"Error writing file: {sftpCommand.Error}";
                if (!sudo && IsPermissionError(sftpCommand.Error))
                    errorMsg += "\nTip: this may be a permissions issue — retry with the sudo parameter set to true.";
                return errorMsg;
            }

            using var verify = lease.Client.CreateCommand($"wc -c < {EscapeArg(path)}");
            var size = verify.Execute().Trim();

            return $"Written {size} bytes to {path} on {lease.Session.DisplayName}";
        }
    }

    [McpServerTool(Name = "ssh_upload"),
     Description("Upload a local file to a remote system via SCP. Credentials are cached after first use.")]
    public static string SshUpload(
        [Description("Name of the session profile")] string session,
        [Description("Path to the local file to upload")] string localPath,
        [Description("Destination path on the remote system")] string remotePath,
        [Description("SSH username (optional)")] string? username = null,
        [Description("SSH password or key passphrase (optional, cached after first use)")] string? password = null,
        [Description("Overwrite if the remote file already exists (default: false)")] bool overwrite = false)
    {
        if (!File.Exists(localPath))
            throw new FileNotFoundException($"Local file not found: {localPath}", localPath);

        if (!TryAcquire(session, username, password, 30, out var lease, out var authError))
            return authError!;

        using (lease)
        {
            if (!overwrite)
            {
                using var check = lease.Client.CreateCommand($"test -e {EscapeArg(remotePath)} && echo EXISTS");
                var checkResult = check.Execute().Trim();
                if (checkResult == "EXISTS")
                    return $"File already exists: {remotePath} on {lease.Session.DisplayName}. Set overwrite to true to replace it.";
            }

            var scpClient = new ScpClient(lease.Client.ConnectionInfo);
            try
            {
                scpClient.Connect();

                using var fileStream = File.OpenRead(localPath);
                scpClient.Upload(fileStream, remotePath);

                var size = new FileInfo(localPath).Length;
                return $"Uploaded {localPath} ({size:N0} bytes) to {lease.Session.DisplayName}:{remotePath}";
            }
            finally
            {
                scpClient.Disconnect();
                scpClient.Dispose();
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
        [Description("SSH password or key passphrase (optional, cached after first use)")] string? password = null,
        [Description("Overwrite if the local file already exists (default: false)")] bool overwrite = false)
    {
        if (!overwrite && File.Exists(localPath))
            return $"File already exists: {localPath}. Set overwrite to true to replace it.";

        if (!TryAcquire(session, username, password, 30, out var lease, out var authError))
            return authError!;

        using (lease)
        {
            var scpClient = new ScpClient(lease.Client.ConnectionInfo);
            try
            {
                scpClient.Connect();

                var dir = Path.GetDirectoryName(localPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                using var fileStream = File.Create(localPath);
                scpClient.Download(remotePath, fileStream);

                var size = new FileInfo(localPath).Length;
                return $"Downloaded {lease.Session.DisplayName}:{remotePath} ({size:N0} bytes) to {localPath}";
            }
            finally
            {
                scpClient.Disconnect();
                scpClient.Dispose();
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

    [McpServerTool(Name = "ssh_disconnect"),
     Description("Explicitly disconnect a pooled SSH session. " +
                  "Useful when a connection is stuck or you want to force a fresh handshake on the next call.")]
    public static string SshDisconnect(
        [Description("Name of the session profile to disconnect")] string session)
    {
        var removed = SshConnectionPool.Disconnect(session);
        return removed
            ? $"Disconnected pooled session '{session}'."
            : $"No pooled connection for session '{session}' (nothing to disconnect).";
    }

    [McpServerTool(Name = "ssh_pool_status"),
     Description("Show the current state of the SSH connection pool: which sessions are live, idle time, and whether any are in use.")]
    public static string SshPoolStatus()
    {
        var snapshot = SshConnectionPool.Snapshot();
        if (snapshot.Count == 0)
            return "SSH connection pool is empty.";

        var sb = new StringBuilder();
        sb.AppendLine($"SSH connection pool ({snapshot.Count} session{(snapshot.Count == 1 ? "" : "s")}):");
        sb.AppendLine();
        sb.AppendLine($"  {"Session",-25} {"Host",-35} {"Connected",-10} {"InUse",-6} Idle");
        foreach (var entry in snapshot.OrderBy(e => e.SessionName))
        {
            var idle = DateTime.UtcNow - entry.LastUsedUtc;
            sb.AppendLine(
                $"  {entry.SessionName,-25} {entry.Host,-35} " +
                $"{(entry.Connected ? "yes" : "no"),-10} {(entry.InUse ? "yes" : "no"),-6} " +
                $"{FormatIdle(idle)}");
        }
        return sb.ToString();
    }

    private static string FormatIdle(TimeSpan t)
    {
        if (t.TotalSeconds < 60) return $"{(int)t.TotalSeconds}s";
        if (t.TotalMinutes < 60) return $"{(int)t.TotalMinutes}m";
        return $"{t.TotalHours:F1}h";
    }

    /// <summary>
    /// Wrap pool acquisition with friendly auth-error messages for the tool layer.
    /// </summary>
    private static bool TryAcquire(
        string session, string? username, string? password, int timeout,
        out SshConnectionPool.Lease lease, out string? error)
    {
        try
        {
            lease = SshConnectionPool.Acquire(session, username, password, timeout);
            error = null;
            return true;
        }
        catch (SshAuthenticationException)
        {
            SshConnectionFactory.ClearCredential(ResolveSessionName(session));
            SshConnectionPool.Disconnect(session);
            lease = default;
            error = $"Authentication failed for session '{session}'. " +
                    "Please call again with the password parameter. It will be cached for subsequent calls.";
            return false;
        }
        catch (SshPassPhraseNullOrEmptyException)
        {
            lease = default;
            error = $"The private key for session '{session}' requires a passphrase. " +
                    "Please call again with the password parameter set to the key passphrase.";
            return false;
        }
    }

    private static string ResolveSessionName(string session)
    {
        return SshConnectionFactory.FindSession(session)?.DisplayName ?? session;
    }

    private static bool IsPermissionError(string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) return false;
        var lower = output.ToLowerInvariant();
        return lower.Contains("permission denied")
            || lower.Contains("operation not permitted")
            || lower.Contains("access denied")
            || lower.Contains("not permitted")
            || lower.Contains("must be run as root")
            || lower.Contains("requires superuser")
            || lower.Contains("unable to connect to the docker daemon");
    }

    private static string EscapeArg(string arg)
    {
        return "'" + arg.Replace("'", "'\\''") + "'";
    }
}
