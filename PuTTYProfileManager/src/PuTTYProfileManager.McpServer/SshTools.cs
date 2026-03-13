using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using PuTTYProfileManager.Core.Services;
using Renci.SshNet;

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
                  "Use list_sessions first to see available profiles.")]
    public static string SshExecute(
        [Description("Name of the session profile to connect to")] string session,
        [Description("Shell command to execute on the remote system")] string command,
        [Description("SSH username (optional, uses session or current user if omitted)")] string? username = null,
        [Description("SSH password (optional, tries key-based auth first)")] string? password = null,
        [Description("Command timeout in seconds (default: 30)")] int timeout = 30)
    {
        var puttySession = SshConnectionFactory.FindSession(session)
            ?? throw new InvalidOperationException(
                $"Session '{session}' not found. Use list_sessions to see available profiles.");

        using var client = SshConnectionFactory.CreateClient(puttySession, username, password);
        client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(timeout);

        client.Connect();

        try
        {
            using var cmd = client.CreateCommand(command);
            cmd.CommandTimeout = TimeSpan.FromSeconds(timeout);
            var result = cmd.Execute();
            var stderr = cmd.Error;
            var exitCode = cmd.ExitStatus;

            var sb = new StringBuilder();
            sb.AppendLine($"[{puttySession.DisplayName}] $ {command}");
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
            client.Disconnect();
        }
    }

    [McpServerTool(Name = "ssh_execute_multi"),
     Description("Execute multiple commands sequentially on a remote system via SSH, " +
                  "reusing a single connection. Returns output for each command.")]
    public static string SshExecuteMulti(
        [Description("Name of the session profile to connect to")] string session,
        [Description("List of shell commands to execute in order")] string[] commands,
        [Description("SSH username (optional)")] string? username = null,
        [Description("SSH password (optional)")] string? password = null,
        [Description("Per-command timeout in seconds (default: 30)")] int timeout = 30)
    {
        var puttySession = SshConnectionFactory.FindSession(session)
            ?? throw new InvalidOperationException(
                $"Session '{session}' not found. Use list_sessions to see available profiles.");

        using var client = SshConnectionFactory.CreateClient(puttySession, username, password);
        client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(timeout);

        client.Connect();

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Connected to {puttySession.DisplayName} ({puttySession.HostName})");
            sb.AppendLine();

            foreach (var command in commands)
            {
                sb.AppendLine($"$ {command}");

                using var cmd = client.CreateCommand(command);
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
            client.Disconnect();
        }
    }

    [McpServerTool(Name = "ssh_read_file"),
     Description("Read the contents of a file on a remote system via SSH.")]
    public static string SshReadFile(
        [Description("Name of the session profile")] string session,
        [Description("Absolute path to the file on the remote system")] string path,
        [Description("SSH username (optional)")] string? username = null,
        [Description("SSH password (optional)")] string? password = null)
    {
        return SshExecute(session, $"cat {EscapeArg(path)}", username, password);
    }

    [McpServerTool(Name = "ssh_write_file"),
     Description("Write content to a file on a remote system via SSH. Creates or overwrites the file.")]
    public static string SshWriteFile(
        [Description("Name of the session profile")] string session,
        [Description("Absolute path to the file on the remote system")] string path,
        [Description("Content to write to the file")] string content,
        [Description("SSH username (optional)")] string? username = null,
        [Description("SSH password (optional)")] string? password = null)
    {
        var puttySession = SshConnectionFactory.FindSession(session)
            ?? throw new InvalidOperationException($"Session '{session}' not found.");

        using var client = SshConnectionFactory.CreateClient(puttySession, username, password);
        client.Connect();

        try
        {
            using var sftpCommand = client.CreateCommand(
                $"cat > {EscapeArg(path)} << 'PUTTYMGR_EOF'\n{content}\nPUTTYMGR_EOF");
            sftpCommand.CommandTimeout = TimeSpan.FromSeconds(30);
            sftpCommand.Execute();

            if (sftpCommand.ExitStatus != 0)
                return $"Error writing file: {sftpCommand.Error}";

            using var verify = client.CreateCommand($"wc -c < {EscapeArg(path)}");
            var size = verify.Execute().Trim();

            return $"Written {size} bytes to {path} on {puttySession.DisplayName}";
        }
        finally
        {
            client.Disconnect();
        }
    }

    [McpServerTool(Name = "ssh_upload"),
     Description("Upload a local file to a remote system via SCP.")]
    public static string SshUpload(
        [Description("Name of the session profile")] string session,
        [Description("Path to the local file to upload")] string localPath,
        [Description("Destination path on the remote system")] string remotePath,
        [Description("SSH username (optional)")] string? username = null,
        [Description("SSH password (optional)")] string? password = null)
    {
        if (!File.Exists(localPath))
            throw new FileNotFoundException($"Local file not found: {localPath}", localPath);

        var puttySession = SshConnectionFactory.FindSession(session)
            ?? throw new InvalidOperationException($"Session '{session}' not found.");

        using var client = SshConnectionFactory.CreateClient(puttySession, username, password);
        using var scpClient = new ScpClient(client.ConnectionInfo);

        scpClient.Connect();

        try
        {
            using var fileStream = File.OpenRead(localPath);
            scpClient.Upload(fileStream, remotePath);

            var size = new FileInfo(localPath).Length;
            return $"Uploaded {localPath} ({size:N0} bytes) to {puttySession.DisplayName}:{remotePath}";
        }
        finally
        {
            scpClient.Disconnect();
        }
    }

    [McpServerTool(Name = "ssh_download"),
     Description("Download a file from a remote system via SCP.")]
    public static string SshDownload(
        [Description("Name of the session profile")] string session,
        [Description("Path to the file on the remote system")] string remotePath,
        [Description("Local destination path")] string localPath,
        [Description("SSH username (optional)")] string? username = null,
        [Description("SSH password (optional)")] string? password = null)
    {
        var puttySession = SshConnectionFactory.FindSession(session)
            ?? throw new InvalidOperationException($"Session '{session}' not found.");

        using var client = SshConnectionFactory.CreateClient(puttySession, username, password);
        using var scpClient = new ScpClient(client.ConnectionInfo);

        scpClient.Connect();

        try
        {
            var dir = Path.GetDirectoryName(localPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            using var fileStream = File.Create(localPath);
            scpClient.Download(remotePath, fileStream);

            var size = new FileInfo(localPath).Length;
            return $"Downloaded {puttySession.DisplayName}:{remotePath} ({size:N0} bytes) to {localPath}";
        }
        finally
        {
            scpClient.Disconnect();
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

    private static string EscapeArg(string arg)
    {
        return "'" + arg.Replace("'", "'\\''") + "'";
    }
}
