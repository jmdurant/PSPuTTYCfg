using PuTTYProfileManager.Core.Models;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace PuTTYProfileManager.McpServer;

/// <summary>
/// Pool of live SshClient connections keyed by session name.
/// Reuses one authenticated connection across many tool calls so we avoid a
/// TCP + SSH handshake per command.
/// </summary>
public static class SshConnectionPool
{
    internal sealed class Entry
    {
        public required SshClient Client { get; set; }
        public required PuttySession Session { get; init; }
        public required SemaphoreSlim Gate { get; init; }
        public DateTime LastUsedUtc { get; set; }
    }

    private static readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object _entriesLock = new();

    private static readonly TimeSpan _keepAlive = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan _idleTimeout = TimeSpan.FromMinutes(10);
    private static readonly Timer _reaper = new(ReapIdle, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

    /// <summary>
    /// Leases a connected SshClient for one operation. Dispose the lease to return it.
    /// </summary>
    public static Lease Acquire(string sessionName, string? username, string? password, int connectTimeoutSeconds = 30)
    {
        var puttySession = SshConnectionFactory.FindSession(sessionName)
            ?? throw new InvalidOperationException(
                $"Session '{sessionName}' not found. Use list_sessions to see available profiles.");

        var key = puttySession.DisplayName;
        Entry entry;

        lock (_entriesLock)
        {
            if (!_entries.TryGetValue(key, out var existing))
            {
                existing = new Entry
                {
                    Client = null!,
                    Session = puttySession,
                    Gate = new SemaphoreSlim(1, 1),
                    LastUsedUtc = DateTime.UtcNow,
                };
                _entries[key] = existing;
            }
            entry = existing;
        }

        entry.Gate.Wait();

        try
        {
            var effectivePassword = SshConnectionFactory.ResolvePassword(puttySession.DisplayName, password);

            if (entry.Client is null || !entry.Client.IsConnected)
            {
                entry.Client?.Dispose();
                var client = SshConnectionFactory.CreateClient(puttySession, username, effectivePassword);
                client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(connectTimeoutSeconds);
                client.KeepAliveInterval = _keepAlive;
                client.Connect();
                entry.Client = client;
            }

            entry.LastUsedUtc = DateTime.UtcNow;
            return Lease.FromEntry(entry);
        }
        catch
        {
            // On failure we must release the gate; the caller will see the exception.
            entry.Gate.Release();
            throw;
        }
    }

    /// <summary>
    /// Explicitly disconnect and drop a session from the pool. Returns true if one was removed.
    /// </summary>
    public static bool Disconnect(string sessionName)
    {
        var puttySession = SshConnectionFactory.FindSession(sessionName);
        var key = puttySession?.DisplayName ?? sessionName;

        Entry? entry;
        lock (_entriesLock)
        {
            if (!_entries.TryGetValue(key, out entry))
                return false;
            _entries.Remove(key);
        }

        DisposeEntry(entry);
        return true;
    }

    /// <summary>
    /// Disconnect every pooled session. Called on process shutdown.
    /// </summary>
    public static void DisconnectAll()
    {
        List<Entry> snapshot;
        lock (_entriesLock)
        {
            snapshot = _entries.Values.ToList();
            _entries.Clear();
        }

        foreach (var entry in snapshot)
            DisposeEntry(entry);
    }

    /// <summary>
    /// Snapshot of pool state for the ssh_pool_status tool.
    /// </summary>
    public static IReadOnlyList<PoolEntryStatus> Snapshot()
    {
        lock (_entriesLock)
        {
            return _entries.Values.Select(e => new PoolEntryStatus(
                e.Session.DisplayName,
                e.Session.HostName ?? "",
                e.Client is not null && e.Client.IsConnected,
                e.LastUsedUtc,
                e.Gate.CurrentCount == 0
            )).ToList();
        }
    }

    private static void ReapIdle(object? _)
    {
        var now = DateTime.UtcNow;
        List<Entry> expired = new();

        lock (_entriesLock)
        {
            foreach (var kvp in _entries.ToList())
            {
                // Skip entries currently in use.
                if (kvp.Value.Gate.CurrentCount == 0) continue;
                if (now - kvp.Value.LastUsedUtc <= _idleTimeout) continue;

                expired.Add(kvp.Value);
                _entries.Remove(kvp.Key);
            }
        }

        foreach (var entry in expired)
            DisposeEntry(entry);
    }

    private static void DisposeEntry(Entry entry)
    {
        try
        {
            if (entry.Client is not null)
            {
                if (entry.Client.IsConnected)
                    entry.Client.Disconnect();
                entry.Client.Dispose();
            }
        }
        catch (SshException) { /* swallow — we're tearing down */ }
        catch (ObjectDisposedException) { /* already gone */ }
    }

    /// <summary>
    /// Holds an exclusive lease on a pooled SshClient. Dispose to return it to the pool.
    /// </summary>
    public readonly struct Lease : IDisposable
    {
        private readonly Entry _entry;

        // Private ctor + same-class factory: avoids CS0051 (public struct with non-public param type).
        private Lease(Entry entry) { _entry = entry; }

        internal static Lease FromEntry(Entry entry) => new(entry);

        public SshClient Client => _entry.Client;
        public PuttySession Session => _entry.Session;

        public void Dispose()
        {
            if (_entry is null) return;
            _entry.LastUsedUtc = DateTime.UtcNow;
            _entry.Gate.Release();
        }
    }

    public sealed record PoolEntryStatus(
        string SessionName,
        string Host,
        bool Connected,
        DateTime LastUsedUtc,
        bool InUse
    );
}
