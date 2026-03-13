using Microsoft.Win32;
using PuTTYProfileManager.Core.Models;
using PuTTYProfileManager.Core.Services;

namespace PuTTYProfileManager.Core.Tests;

public class SessionDiffServiceTests
{
    private static PuttySession CreateSession(string name, params (string key, object value)[] settings)
    {
        var session = new PuttySession { EncodedName = name };
        foreach (var (key, value) in settings)
        {
            var kind = value is int ? RegistryValueKind.DWord : RegistryValueKind.String;
            session.Values.Add(new RegistrySettingValue { Name = key, Kind = kind, Value = value });
        }
        return session;
    }

    [Fact]
    public void Compare_IdenticalSessions_NoDifferences()
    {
        var local = CreateSession("test", ("HostName", "host.com"), ("PortNumber", 22));
        var archive = CreateSession("test", ("HostName", "host.com"), ("PortNumber", 22));

        var diff = SessionDiffService.Compare(local, archive);

        Assert.False(diff.HasDifferences);
        Assert.Equal(0, diff.TotalDifferences);
        Assert.Empty(diff.ChangedSettings);
        Assert.Empty(diff.AddedInArchive);
        Assert.Empty(diff.RemovedInArchive);
    }

    [Fact]
    public void Compare_ChangedValue_DetectsChange()
    {
        var local = CreateSession("test", ("HostName", "old.com"), ("PortNumber", 22));
        var archive = CreateSession("test", ("HostName", "new.com"), ("PortNumber", 22));

        var diff = SessionDiffService.Compare(local, archive);

        Assert.True(diff.HasDifferences);
        Assert.Single(diff.ChangedSettings);
        Assert.Equal("HostName", diff.ChangedSettings[0].Name);
        Assert.Equal("old.com", diff.ChangedSettings[0].LocalValue);
        Assert.Equal("new.com", diff.ChangedSettings[0].ArchiveValue);
    }

    [Fact]
    public void Compare_AddedSetting_Detected()
    {
        var local = CreateSession("test", ("HostName", "host.com"));
        var archive = CreateSession("test", ("HostName", "host.com"), ("PortNumber", 2222));

        var diff = SessionDiffService.Compare(local, archive);

        Assert.True(diff.HasDifferences);
        Assert.Empty(diff.ChangedSettings);
        Assert.Single(diff.AddedInArchive);
        Assert.Equal("PortNumber", diff.AddedInArchive[0].Name);
    }

    [Fact]
    public void Compare_RemovedSetting_Detected()
    {
        var local = CreateSession("test", ("HostName", "host.com"), ("PortNumber", 22));
        var archive = CreateSession("test", ("HostName", "host.com"));

        var diff = SessionDiffService.Compare(local, archive);

        Assert.True(diff.HasDifferences);
        Assert.Empty(diff.ChangedSettings);
        Assert.Single(diff.RemovedInArchive);
        Assert.Equal("PortNumber", diff.RemovedInArchive[0].Name);
    }

    [Fact]
    public void Compare_MultipleChanges_AllDetected()
    {
        var local = CreateSession("test",
            ("HostName", "old.com"),
            ("PortNumber", 22),
            ("Protocol", "ssh"),
            ("LocalOnly", "value"));
        var archive = CreateSession("test",
            ("HostName", "new.com"),
            ("PortNumber", 2222),
            ("Protocol", "ssh"),
            ("NewSetting", "added"));

        var diff = SessionDiffService.Compare(local, archive);

        Assert.True(diff.HasDifferences);
        Assert.Equal(2, diff.ChangedSettings.Count); // HostName, PortNumber
        Assert.Single(diff.AddedInArchive); // NewSetting
        Assert.Single(diff.RemovedInArchive); // LocalOnly
        Assert.Equal(4, diff.TotalDifferences);
    }

    [Fact]
    public void Compare_CaseInsensitiveSettingNames()
    {
        var local = CreateSession("test", ("hostname", "host.com"));
        var archive = CreateSession("test", ("HostName", "host.com"));

        var diff = SessionDiffService.Compare(local, archive);

        Assert.False(diff.HasDifferences);
    }

    [Fact]
    public void Compare_BinaryValues_Compared()
    {
        var local = new PuttySession
        {
            EncodedName = "test",
            Values = [new RegistrySettingValue { Name = "Blob", Kind = RegistryValueKind.Binary, Value = new byte[] { 1, 2, 3 } }]
        };
        var archive = new PuttySession
        {
            EncodedName = "test",
            Values = [new RegistrySettingValue { Name = "Blob", Kind = RegistryValueKind.Binary, Value = new byte[] { 1, 2, 4 } }]
        };

        var diff = SessionDiffService.Compare(local, archive);

        Assert.True(diff.HasDifferences);
        Assert.Single(diff.ChangedSettings);
        Assert.Equal("Blob", diff.ChangedSettings[0].Name);
    }

    [Fact]
    public void Compare_BinaryValues_IdenticalAreEqual()
    {
        var local = new PuttySession
        {
            EncodedName = "test",
            Values = [new RegistrySettingValue { Name = "Blob", Kind = RegistryValueKind.Binary, Value = new byte[] { 1, 2, 3 } }]
        };
        var archive = new PuttySession
        {
            EncodedName = "test",
            Values = [new RegistrySettingValue { Name = "Blob", Kind = RegistryValueKind.Binary, Value = new byte[] { 1, 2, 3 } }]
        };

        var diff = SessionDiffService.Compare(local, archive);
        Assert.False(diff.HasDifferences);
    }

    [Fact]
    public void Compare_PreservesTimestamps()
    {
        var localTime = new DateTime(2026, 3, 10, 14, 30, 0);
        var archiveTime = new DateTime(2026, 3, 1, 9, 0, 0);

        var diff = SessionDiffService.Compare(
            CreateSession("test", ("HostName", "a.com")),
            CreateSession("test", ("HostName", "b.com")),
            localTime, archiveTime);

        Assert.Equal(localTime, diff.LocalLastModified);
        Assert.Equal(archiveTime, diff.ArchiveDate);
    }

    [Fact]
    public void Compare_DisplayName_UsesLocalSession()
    {
        var local = CreateSession("My%20Server", ("HostName", "host.com"));
        var archive = CreateSession("My%20Server", ("HostName", "host.com"));

        var diff = SessionDiffService.Compare(local, archive);
        Assert.Equal("My Server", diff.DisplayName);
    }

    [Fact]
    public void CompareAll_OnlyReturnsDifferences()
    {
        var sessionsDir = Path.Combine(Path.GetTempPath(), $"diff_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(sessionsDir);

        try
        {
            var svc = new UnixSessionService(sessionsDir);

            // Write two local sessions
            svc.WriteSession(CreateSession("same", ("HostName", "host.com"), ("PortNumber", 22)));
            svc.WriteSession(CreateSession("different", ("HostName", "old.com"), ("PortNumber", 22)));

            var archiveSessions = new List<PuttySession>
            {
                CreateSession("same", ("HostName", "host.com"), ("PortNumber", 22)),
                CreateSession("different", ("HostName", "new.com"), ("PortNumber", 22)),
                CreateSession("brand-new", ("HostName", "new.com"))
            };

            var diffs = SessionDiffService.CompareAll(archiveSessions, svc);

            // Only "different" should appear — "same" is identical, "brand-new" doesn't exist locally
            Assert.Single(diffs);
            Assert.Equal("different", diffs[0].ArchiveSession.EncodedName);
        }
        finally
        {
            Directory.Delete(sessionsDir, true);
        }
    }
}
