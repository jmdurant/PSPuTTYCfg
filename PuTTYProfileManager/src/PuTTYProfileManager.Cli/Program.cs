using System.Runtime.InteropServices;
using PuTTYProfileManager.Core.Models;
using PuTTYProfileManager.Core.Services;

namespace PuTTYProfileManager.Cli;

class Program
{
    static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "backup" => RunBackup(args[1..]),
                "restore" => RunRestore(args[1..]),
                "list" => RunList(args[1..]),
                "diff" => RunDiff(args[1..]),
                "help" or "--help" or "-h" => PrintUsage(),
                _ => Error($"Unknown command: {args[0]}")
            };
        }
        catch (FileNotFoundException ex)
        {
            return Error($"File not found: {ex.FileName ?? ex.Message}");
        }
        catch (IndexOutOfRangeException)
        {
            return Error("Missing value for option. Run 'puttymgr help' for usage.");
        }
        catch (ICSharpCode.SharpZipLib.Zip.ZipException ex) when (ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("Invalid", StringComparison.OrdinalIgnoreCase))
        {
            return Error("Incorrect password or corrupted archive.");
        }
        catch (UnauthorizedAccessException ex)
        {
            return Error($"Access denied: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Unexpected error: {ex.Message}");
            Console.Error.WriteLine($"  {ex.GetType().Name}");
            return 1;
        }
    }

    static int RunBackup(string[] args)
    {
        string? outputPath = null;
        string? password = null;
        string? filter = null;
        string? sessionsDir = null;
        bool includeFiles = true;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-o" or "--output":
                    outputPath = args[++i];
                    break;
                case "-p" or "--password":
                    password = args[++i];
                    break;
                case "-f" or "--filter":
                    filter = args[++i];
                    break;
                case "--sessions-dir":
                    sessionsDir = args[++i];
                    break;
                case "--no-files":
                    includeFiles = false;
                    break;
                default:
                    return Error($"Unknown option: {args[i]}");
            }
        }

        if (outputPath is null)
        {
            outputPath = $"PuTTY_Backup_{DateTime.Now:yyyy-MM-dd_HHmmss}.zip";
        }

        var sessionService = CreateSessionService(sessionsDir);
        var archiveService = new SessionArchiveService();

        Console.WriteLine("Reading sessions...");
        var sessions = sessionService.GetAllSessions();

        if (filter is not null)
        {
            sessions = sessions.Where(s => MatchesFilter(s.DisplayName, filter)).ToList();
        }

        if (sessions.Count == 0)
        {
            Console.WriteLine("No sessions found.");
            return 0;
        }

        Console.WriteLine($"Found {sessions.Count} session(s):");
        foreach (var s in sessions.OrderBy(s => s.DisplayName))
        {
            Console.WriteLine($"  {s.DisplayName,-30} {s.Summary}");
        }

        if (includeFiles)
        {
            var linkedFiles = LinkedFileService.GetLinkedFiles(sessions);
            if (linkedFiles.Count > 0)
            {
                Console.WriteLine($"\nLinked files ({linkedFiles.Count}):");
                foreach (var f in linkedFiles)
                {
                    var status = f.Exists ? $"OK ({f.DisplaySize})" : "MISSING";
                    Console.WriteLine($"  [{status,-15}] {f.SettingLabel,-20} {f.OriginalPath}");
                }
            }
        }

        Console.WriteLine($"\nBacking up to: {outputPath}");
        archiveService.ExportToZip(outputPath, sessions, includeFiles, password);

        var protectedText = password is not null ? " (password protected)" : "";
        Console.WriteLine($"Done! Backed up {sessions.Count} session(s){protectedText}.");
        return 0;
    }

    static int RunRestore(string[] args)
    {
        string? inputPath = null;
        string? password = null;
        string? sessionsDir = null;
        string? filesDir = null;
        bool force = false;
        bool useSshConfig = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-i" or "--input":
                    inputPath = args[++i];
                    break;
                case "-p" or "--password":
                    password = args[++i];
                    break;
                case "--sessions-dir":
                    sessionsDir = args[++i];
                    break;
                case "--files-dir":
                    filesDir = args[++i];
                    break;
                case "--force":
                    force = true;
                    break;
                case "--ssh-config":
                    useSshConfig = true;
                    break;
                default:
                    return Error($"Unknown option: {args[i]}");
            }
        }

        if (inputPath is null)
            return Error("--input (-i) is required");

        // On non-Windows, default to writing ~/.ssh/config unless --sessions-dir is specified
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && sessionsDir is null)
            useSshConfig = true;

        var sessionService = CreateSessionService(sessionsDir, useSshConfig);
        var archiveService = new SessionArchiveService();

        if (archiveService.IsPasswordProtected(inputPath) && password is null)
        {
            Console.Write("Archive is password protected. Enter password: ");
            password = ReadPassword();
            Console.WriteLine();
        }

        Console.WriteLine("Reading archive...");
        var contents = archiveService.ImportFromZip(inputPath, password);

        if (contents.HasErrors)
        {
            Console.Error.WriteLine($"\nWarning: {contents.ValidationErrors.Count} issue(s) found in archive:");
            foreach (var err in contents.ValidationErrors)
                Console.Error.WriteLine($"  - {err}");
        }

        if (contents.Sessions.Count == 0)
        {
            Console.WriteLine("No sessions found in archive.");
            return 0;
        }

        Console.WriteLine($"Found {contents.Sessions.Count} session(s):");
        foreach (var s in contents.Sessions.OrderBy(s => s.DisplayName))
        {
            var exists = sessionService.SessionExists(s.EncodedName);
            var tag = exists ? " [EXISTS]" : "";
            Console.WriteLine($"  {s.DisplayName,-30} {s.Summary}{tag}");
        }

        // Determine which sessions to restore
        var sessionsToRestore = new List<PuttySession>();
        var skipped = 0;

        if (!force)
        {
            var archiveDate = File.GetLastWriteTime(inputPath);
            var diffs = SessionDiffService.CompareAll(contents.Sessions, sessionService, archiveDate);
            var diffLookup = diffs.ToDictionary(d => d.ArchiveSession.EncodedName, StringComparer.OrdinalIgnoreCase);

            foreach (var session in contents.Sessions)
            {
                if (!sessionService.SessionExists(session.EncodedName))
                {
                    sessionsToRestore.Add(session);
                    continue;
                }

                if (diffLookup.TryGetValue(session.EncodedName, out var diff) && diff.HasDifferences)
                {
                    Console.WriteLine($"\n--- {session.DisplayName} ---");

                    if (diff.LocalLastModified.HasValue)
                        Console.WriteLine($"  Local modified:   {diff.LocalLastModified:yyyy-MM-dd HH:mm}");
                    if (diff.ArchiveDate.HasValue)
                        Console.WriteLine($"  Archive created:  {diff.ArchiveDate:yyyy-MM-dd HH:mm}");

                    Console.WriteLine($"  {diff.TotalDifferences} difference(s):");

                    foreach (var change in diff.ChangedSettings)
                    {
                        Console.WriteLine($"    {change.Name,-25} {change.LocalValue,-25} -> {change.ArchiveValue}");
                    }
                    foreach (var added in diff.AddedInArchive)
                    {
                        Console.WriteLine($"    {added.Name,-25} {"(not set)",-25} -> {added.Value}");
                    }
                    foreach (var removed in diff.RemovedInArchive)
                    {
                        Console.WriteLine($"    {removed.Name,-25} {removed.Value?.ToString() ?? "",-25} -> (removed)");
                    }

                    Console.Write("  Overwrite with archive version? [y/N/q]: ");
                    var response = Console.ReadLine()?.Trim().ToLowerInvariant();
                    if (response == "q" || response == "quit")
                    {
                        Console.WriteLine("Restore cancelled.");
                        return 0;
                    }
                    if (response == "y" || response == "yes")
                        sessionsToRestore.Add(session);
                    else
                        skipped++;
                }
                else
                {
                    // Exists but identical — skip silently
                    skipped++;
                }
            }
        }
        else
        {
            sessionsToRestore.AddRange(contents.Sessions);
        }

        if (sessionsToRestore.Count == 0)
        {
            Console.WriteLine("\nNothing to restore (all sessions identical or skipped).");
            return 0;
        }

        // Extract linked files
        if (contents.LinkedFileEntries.Count > 0)
        {
            filesDir ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");

            Console.WriteLine($"\nExtracting {contents.LinkedFileEntries.Count} linked file(s) to: {filesDir}");
            archiveService.ExtractLinkedFiles(inputPath, filesDir, password);

            foreach (var session in sessionsToRestore)
            {
                LinkedFileService.UpdateSessionPaths(session, filesDir, contents.FileMapping);
            }
        }

        // Write sessions
        Console.WriteLine("\nRestoring sessions...");
        foreach (var session in sessionsToRestore)
        {
            sessionService.WriteSession(session);
            Console.WriteLine($"  Restored: {session.DisplayName}");
        }

        var summary = skipped > 0
            ? $"\nDone! Restored {sessionsToRestore.Count} session(s), skipped {skipped}."
            : $"\nDone! Restored {sessionsToRestore.Count} session(s).";
        Console.WriteLine(summary);
        return 0;
    }

    static int RunList(string[] args)
    {
        string? inputPath = null;
        string? password = null;
        string? sessionsDir = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-i" or "--input":
                    inputPath = args[++i];
                    break;
                case "-p" or "--password":
                    password = args[++i];
                    break;
                case "--sessions-dir":
                    sessionsDir = args[++i];
                    break;
                default:
                    return Error($"Unknown option: {args[i]}");
            }
        }

        if (inputPath is not null)
        {
            // List from archive
            var archiveService = new SessionArchiveService();

            if (archiveService.IsPasswordProtected(inputPath) && password is null)
            {
                Console.Write("Archive is password protected. Enter password: ");
                password = ReadPassword();
                Console.WriteLine();
            }

            var contents = archiveService.ImportFromZip(inputPath, password);

            if (contents.HasErrors)
            {
                Console.Error.WriteLine($"\nWarning: {contents.ValidationErrors.Count} issue(s) found in archive:");
                foreach (var err in contents.ValidationErrors)
                    Console.Error.WriteLine($"  - {err}");
            }

            Console.WriteLine($"Archive: {inputPath}");
            Console.WriteLine($"Sessions ({contents.Sessions.Count}):");
            foreach (var s in contents.Sessions.OrderBy(s => s.DisplayName))
            {
                Console.WriteLine($"  {s.DisplayName,-30} {s.Summary,-35} ({s.Values.Count} settings)");
            }

            if (contents.LinkedFileEntries.Count > 0)
            {
                Console.WriteLine($"\nLinked files ({contents.LinkedFileEntries.Count}):");
                foreach (var entry in contents.LinkedFileEntries)
                {
                    Console.WriteLine($"  {Path.GetFileName(entry)}");
                }
            }
        }
        else
        {
            // List from local system
            var sessionService = CreateSessionService(sessionsDir);
            var sessions = sessionService.GetAllSessions();

            var source = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Registry" : "~/.putty/sessions";
            Console.WriteLine($"Source: {source}");
            Console.WriteLine($"Sessions ({sessions.Count}):");
            foreach (var s in sessions.OrderBy(s => s.DisplayName))
            {
                Console.WriteLine($"  {s.DisplayName,-30} {s.Summary,-35} ({s.Values.Count} settings)");
            }

            var linkedFiles = LinkedFileService.GetLinkedFiles(sessions);
            if (linkedFiles.Count > 0)
            {
                Console.WriteLine($"\nLinked files ({linkedFiles.Count}):");
                foreach (var f in linkedFiles)
                {
                    var status = f.Exists ? "OK" : "MISSING";
                    Console.WriteLine($"  [{status,-7}] {f.SettingLabel,-20} {f.OriginalPath}");
                }
            }
        }

        return 0;
    }

    static int RunDiff(string[] args)
    {
        string? inputPath = null;
        string? password = null;
        string? sessionsDir = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-i" or "--input":
                    inputPath = args[++i];
                    break;
                case "-p" or "--password":
                    password = args[++i];
                    break;
                case "--sessions-dir":
                    sessionsDir = args[++i];
                    break;
                default:
                    return Error($"Unknown option: {args[i]}");
            }
        }

        if (inputPath is null)
            return Error("--input (-i) is required");

        var sessionService = CreateSessionService(sessionsDir);
        var archiveService = new SessionArchiveService();

        if (archiveService.IsPasswordProtected(inputPath) && password is null)
        {
            Console.Write("Archive is password protected. Enter password: ");
            password = ReadPassword();
            Console.WriteLine();
        }

        var contents = archiveService.ImportFromZip(inputPath, password);
        var archiveDate = File.GetLastWriteTime(inputPath);
        var diffs = SessionDiffService.CompareAll(contents.Sessions, sessionService, archiveDate);

        // Also report new sessions (in archive but not local)
        var newSessions = contents.Sessions.Where(s => !sessionService.SessionExists(s.EncodedName)).ToList();

        if (diffs.Count == 0 && newSessions.Count == 0)
        {
            Console.WriteLine("No differences found. Local sessions match the archive.");
            return 0;
        }

        if (newSessions.Count > 0)
        {
            Console.WriteLine($"New sessions in archive ({newSessions.Count}):");
            foreach (var s in newSessions.OrderBy(s => s.DisplayName))
            {
                Console.WriteLine($"  + {s.DisplayName,-30} {s.Summary}");
            }
        }

        // Sessions that exist locally but are identical
        var identical = contents.Sessions
            .Where(s => sessionService.SessionExists(s.EncodedName))
            .Count(s => !diffs.Any(d => d.ArchiveSession.EncodedName == s.EncodedName));

        if (identical > 0)
            Console.WriteLine($"\nIdentical sessions: {identical}");

        if (diffs.Count > 0)
        {
            Console.WriteLine($"\nSessions with differences ({diffs.Count}):");

            foreach (var diff in diffs.OrderBy(d => d.DisplayName))
            {
                Console.WriteLine($"\n  {diff.DisplayName}");

                if (diff.LocalLastModified.HasValue)
                    Console.Write($"    Local: {diff.LocalLastModified:yyyy-MM-dd HH:mm}");
                if (diff.ArchiveDate.HasValue)
                    Console.Write($"    Archive: {diff.ArchiveDate:yyyy-MM-dd HH:mm}");
                if (diff.LocalLastModified.HasValue || diff.ArchiveDate.HasValue)
                    Console.WriteLine();

                foreach (var change in diff.ChangedSettings)
                {
                    Console.WriteLine($"    ~ {change.Name,-25} {change.LocalValue,-25} -> {change.ArchiveValue}");
                }
                foreach (var added in diff.AddedInArchive)
                {
                    Console.WriteLine($"    + {added.Name,-25} {added.Value}");
                }
                foreach (var removed in diff.RemovedInArchive)
                {
                    Console.WriteLine($"    - {removed.Name,-25} {removed.Value}");
                }
            }
        }

        return 0;
    }

    static ISessionService CreateSessionService(string? sessionsDir, bool useSshConfig = false)
    {
        if (sessionsDir is not null)
            return new UnixSessionService(sessionsDir);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new RegistrySessionService();

        // On Linux/macOS, prefer ~/.ssh/config (standard OpenSSH format)
        if (useSshConfig)
            return new SshConfigSessionService();

        return new UnixSessionService();
    }

    static bool MatchesFilter(string name, string pattern)
    {
        if (pattern.EndsWith('*'))
            return name.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase);
        if (pattern.StartsWith('*'))
            return name.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase);
        return name.Contains(pattern, StringComparison.OrdinalIgnoreCase);
    }

    static string ReadPassword()
    {
        var password = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(true);
            if (key.Key == ConsoleKey.Enter)
                break;
            if (key.Key == ConsoleKey.Backspace && password.Length > 0)
                password.Length--;
            else if (!char.IsControl(key.KeyChar))
                password.Append(key.KeyChar);
        }
        return password.ToString();
    }

    static int PrintUsage()
    {
        Console.WriteLine("""
            PuTTY Profile Manager CLI - Backup & Restore PuTTY Sessions

            Usage: puttymgr <command> [options]

            Commands:
              backup    Back up PuTTY sessions to a ZIP archive
              restore   Restore PuTTY sessions from a ZIP archive
              list      List sessions (local or from an archive)
              diff      Compare archive sessions against local sessions

            Backup options:
              -o, --output <path>     Output ZIP file path (default: auto-generated)
              -p, --password <pw>     Password to encrypt the archive
              -f, --filter <pattern>  Filter sessions by name (supports * wildcards)
              --no-files              Don't include linked files (PPK keys, certs)
              --sessions-dir <path>   Custom sessions directory (Linux)

            Restore options:
              -i, --input <path>      Input ZIP file path (required)
              -p, --password <pw>     Password to decrypt the archive
              --files-dir <path>      Directory for extracted linked files (default: ~/.ssh)
              --sessions-dir <path>   Custom PuTTY sessions directory
              --ssh-config            Write to ~/.ssh/config (default on Linux/macOS)
              --force                 Overwrite existing sessions without prompting

            List options:
              -i, --input <path>      List sessions from an archive (omit for local)
              -p, --password <pw>     Password for encrypted archive
              --sessions-dir <path>   Custom sessions directory (Linux)

            Diff options:
              -i, --input <path>      Input ZIP file path (required)
              -p, --password <pw>     Password for encrypted archive
              --sessions-dir <path>   Custom sessions directory (Linux)

            Platform:
              Windows   Reads/writes from the Windows Registry (HKCU)
              Linux     Restores to ~/.ssh/config (OpenSSH format) by default
              macOS     Restores to ~/.ssh/config (OpenSSH format) by default

            Examples:
              puttymgr list
              puttymgr backup -o backup.zip
              puttymgr backup -o backup.zip -p secret --filter "prod-*"
              puttymgr restore -i backup.zip
              puttymgr restore -i backup.zip --files-dir /home/user/.ssh
              puttymgr list -i backup.zip
              puttymgr diff -i backup.zip
            """);
        return 0;
    }

    static int Error(string message)
    {
        Console.Error.WriteLine($"Error: {message}");
        Console.Error.WriteLine("Run 'puttymgr help' for usage.");
        return 1;
    }
}
