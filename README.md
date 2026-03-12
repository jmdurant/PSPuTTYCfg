# PuTTY Profile Manager

A cross-platform tool to backup and restore PuTTY session profiles. Available as a Windows GUI (WPF), cross-platform GUI (Avalonia), and CLI.

## Features

- **Backup** PuTTY sessions to password-protected ZIP archives (AES-256)
- **Restore** sessions from archives with overwrite confirmation
- **Linked files** — automatically bundles PPK keys, certificates, X11 auth files, and bell sounds
- **Cross-platform** — CLI and Avalonia GUI work on Windows, macOS, and Linux
- **Windows installer** — MSI installer for the WPF GUI

## Downloads

Grab the latest release from the [Releases](../../releases) page:

| Platform | GUI | CLI |
|---|---|---|
| Windows | `PuTTYProfileManagerSetup.msi` | `puttymgr-cli-win-x64.zip` |
| macOS (Apple Silicon) | `puttymgr-gui-osx-arm64.zip` | `puttymgr-cli-osx-arm64.zip` |
| macOS (Intel) | `puttymgr-gui-osx-x64.zip` | `puttymgr-cli-osx-x64.zip` |
| Linux | `puttymgr-gui-linux-x64.zip` | `puttymgr-cli-linux-x64.zip` |

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

## Session Storage

- **Windows** — Registry: `HKCU\SOFTWARE\SimonTatham\PuTTY\Sessions`
- **Linux/macOS** — Files: `~/.putty/sessions/`

## Building

Requires [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
# Windows GUI (WPF)
dotnet run --project PuTTYProfileManager/src/PuTTYProfileManager

# Cross-platform GUI (Avalonia)
dotnet run --project PuTTYProfileManager/src/PuTTYProfileManager.Avalonia

# CLI
dotnet run --project PuTTYProfileManager/src/PuTTYProfileManager.Cli
```

## License

All content is licensed under the terms of [The MIT License](LICENSE).
