using Microsoft.Win32;
using PuTTYProfileManager.Core.Models;

namespace PuTTYProfileManager.Core.Services;

public static class SessionDiffService
{
    public static SessionDiff Compare(PuttySession local, PuttySession archive, DateTime? localLastModified = null, DateTime? archiveDate = null)
    {
        var localLookup = local.Values.ToDictionary(v => v.Name, StringComparer.OrdinalIgnoreCase);
        var archiveLookup = archive.Values.ToDictionary(v => v.Name, StringComparer.OrdinalIgnoreCase);

        var changed = new List<SettingDiff>();
        var added = new List<RegistrySettingValue>();
        var removed = new List<RegistrySettingValue>();

        // Settings in both — check for changes
        foreach (var archiveVal in archive.Values)
        {
            if (localLookup.TryGetValue(archiveVal.Name, out var localVal))
            {
                if (!ValuesEqual(localVal, archiveVal))
                {
                    changed.Add(new SettingDiff
                    {
                        Name = archiveVal.Name,
                        LocalValue = FormatValue(localVal),
                        ArchiveValue = FormatValue(archiveVal)
                    });
                }
            }
            else
            {
                added.Add(archiveVal);
            }
        }

        // Settings only in local
        foreach (var localVal in local.Values)
        {
            if (!archiveLookup.ContainsKey(localVal.Name))
            {
                removed.Add(localVal);
            }
        }

        return new SessionDiff
        {
            LocalSession = local,
            ArchiveSession = archive,
            LocalLastModified = localLastModified,
            ArchiveDate = archiveDate,
            ChangedSettings = changed,
            AddedInArchive = added,
            RemovedInArchive = removed
        };
    }

    public static List<SessionDiff> CompareAll(
        IEnumerable<PuttySession> archiveSessions,
        ISessionService sessionService,
        DateTime? archiveDate = null)
    {
        var diffs = new List<SessionDiff>();

        foreach (var archiveSession in archiveSessions)
        {
            var local = sessionService.GetSession(archiveSession.EncodedName);
            if (local is null)
                continue;

            var localModified = sessionService.GetSessionLastModified(archiveSession.EncodedName);
            var diff = Compare(local, archiveSession, localModified, archiveDate);

            if (diff.HasDifferences)
                diffs.Add(diff);
        }

        return diffs;
    }

    private static bool ValuesEqual(RegistrySettingValue a, RegistrySettingValue b)
    {
        if (a.Kind != b.Kind)
            return false;

        return (a.Kind, a.Value, b.Value) switch
        {
            (_, null, null) => true,
            (_, null, _) or (_, _, null) => false,
            (RegistryValueKind.Binary, byte[] ab, byte[] bb) => ab.AsSpan().SequenceEqual(bb),
            (RegistryValueKind.MultiString, string[] aa, string[] ba) => aa.SequenceEqual(ba),
            _ => string.Equals(a.Value?.ToString(), b.Value?.ToString(), StringComparison.Ordinal)
        };
    }

    private static string FormatValue(RegistrySettingValue val)
    {
        return val.Kind switch
        {
            RegistryValueKind.Binary when val.Value is byte[] bytes =>
                $"[{bytes.Length} bytes]",
            RegistryValueKind.MultiString when val.Value is string[] arr =>
                string.Join("; ", arr),
            _ => val.Value?.ToString() ?? "(null)"
        };
    }
}
