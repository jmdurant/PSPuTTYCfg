# PuTTY Profile Manager

A cross-platform tool to backup, restore, and remotely manage PuTTY session profiles. Available as a Windows GUI (WPF), cross-platform GUI (Avalonia), CLI, and an MCP server for AI-assisted remote server management.

## Features

- **Backup** PuTTY sessions to password-protected ZIP archives (AES-256)
- **Restore** sessions from archives with overwrite confirmation
- **Linked files** — automatically bundles PPK keys, certificates, X11 auth files, and bell sounds
- **Cross-platform** — CLI and Avalonia GUI work on Windows, macOS, and Linux
- **Windows installer** — MSI installer for the WPF GUI
- **MCP Server** — AI tools can execute commands, transfer files, and manage your servers using your PuTTY/SSH profiles
- **Native PPK support** — reads PuTTY `.ppk` private keys directly (no conversion needed)
- **Credential caching** — SSH passwords and passphrases are cached per session for the duration of a run
- **Auto host-key acceptance** — first-time connections automatically accept the server's host key

## Downloads

Grab the latest release from the [Releases](../../releases) page:

| Platform | GUI | CLI | MCP Server |
|---|---|---|---|
| Windows | `PuTTYProfileManagerSetup.msi` | `puttymgr-cli-win-x64.zip` | `puttymgr-mcp-win-x64.zip` |
| macOS (Apple Silicon) | `puttymgr-gui-osx-arm64.zip` | `puttymgr-cli-osx-arm64.zip` | `puttymgr-mcp-osx-arm64.zip` |
| macOS (Intel) | `puttymgr-gui-osx-x64.zip` | `puttymgr-cli-osx-x64.zip` | `puttymgr-mcp-osx-x64.zip` |
| Linux | `puttymgr-gui-linux-x64.zip` | `puttymgr-cli-linux-x64.zip` | `puttymgr-mcp-linux-x64.zip` |

## CLI Usage

```
puttymgr backup              Backup all sessions
puttymgr backup -o out.zip   Backup to specific file
puttymgr backup --filter dev Backup sessions matching "dev"
puttymgr backup --password   Encrypt the backup (prompts for password)
puttymgr backup --no-files   Skip linked files (PPK keys, etc.)

puttymgr restore backup.zip  Restore sessions from archive
puttymgr restore --force     Overwrite existing sessions without prompting

puttymgr list                List all PuTTY sessions
```

## MCP Server

The MCP server lets AI assistants (like Claude) interact with your remote servers using your existing PuTTY and `~/.ssh/config` session profiles. It reads profiles from the PuTTY registry (Windows) and SSH config (all platforms).

### Setup with Claude Code

The included `.mcp.json` auto-configures the server when you open this project in Claude Code. For other projects, add to your `.mcp.json`:

```json
{
  "mcpServers": {
    "puttymgr": {
      "command": "puttymgr-mcp"
    }
  }
}
```

### Available Tools

| Tool | Description |
|---|---|
| `list_sessions` | List all SSH session profiles with host, port, user, and auth method |
| `ssh_execute` | Execute a command on a remote system via a session profile |
| `ssh_execute_multi` | Execute multiple commands sequentially over a single SSH connection |
| `ssh_read_file` | Read a file from a remote system |
| `ssh_write_file` | Write content to a file on a remote system |
| `ssh_upload` | Upload a local file to a remote system via SCP |
| `ssh_download` | Download a file from a remote system via SCP |
| `ssh_session_info` | Get detailed connection info for a session profile |

### Key Features

- **Session discovery** — automatically reads PuTTY sessions (Windows Registry) and `~/.ssh/config` profiles
- **Native PPK keys** — authenticates with PuTTY `.ppk` files directly, no need to convert to OpenSSH format
- **Credential caching** — passwords and key passphrases are cached per session after first use
- **Sudo support** — all execute and write tools have a `sudo` parameter to run commands with elevated privileges
- **Permission hints** — automatically detects permission-denied errors and suggests using the `sudo` parameter
- **Auto host-key acceptance** — seamlessly connects to servers on first use without manual intervention

### Example Usage (Claude Code)

```
You: "Check disk space on my production server"
Claude: uses ssh_execute with session "prod-server" and command "df -h"

You: "Update the Docker image for nginx"
Claude: uses ssh_execute with session "prod-server", sudo=true,
        and runs docker pull/compose commands
```

## Session Storage

- **Windows** — Registry: `HKCU\SOFTWARE\SimonTatham\PuTTY\Sessions`
- **Linux/macOS** — Files: `~/.putty/sessions/`
- **SSH Config** — `~/.ssh/config` (all platforms, read-only)

## Building

Requires [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
# Windows GUI (WPF)
dotnet run --project PuTTYProfileManager/src/PuTTYProfileManager

# Cross-platform GUI (Avalonia)
dotnet run --project PuTTYProfileManager/src/PuTTYProfileManager.Avalonia

# CLI
dotnet run --project PuTTYProfileManager/src/PuTTYProfileManager.Cli

# MCP Server
dotnet run --project PuTTYProfileManager/src/PuTTYProfileManager.McpServer
```

## License

All content is licensed under the terms of [The MIT License](LICENSE).
