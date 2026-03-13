using Microsoft.Win32;
using PuTTYProfileManager.Core.Models;
using PuTTYProfileManager.Core.Services;

namespace PuTTYProfileManager.Core.Tests;

public class UnixSessionServiceTests : IDisposable
{
    private readonly string _sessionsDir;
    private readonly UnixSessionService _service;

    public UnixSessionServiceTests()
    {
        _sessionsDir = Path.Combine(Path.GetTempPath(), $"puttymgr_sessions_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_sessionsDir);
        _service = new UnixSessionService(_sessionsDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_sessionsDir))
            Directory.Delete(_sessionsDir, true);
    }

    [Fact]
    public void GetAllSessions_EmptyDirectory_ReturnsEmpty()
    {
        var sessions = _service.GetAllSessions();
        Assert.Empty(sessions);
    }

    [Fact]
    public void WriteAndRead_RoundTrip()
    {
        var session = new PuttySession
        {
            EncodedName = "myserver",
            Values =
            [
                new RegistrySettingValue { Name = "HostName", Kind = RegistryValueKind.String, Value = "example.com" },
                new RegistrySettingValue { Name = "PortNumber", Kind = RegistryValueKind.DWord, Value = 22 },
                new RegistrySettingValue { Name = "Protocol", Kind = RegistryValueKind.String, Value = "ssh" }
            ]
        };

        _service.WriteSession(session);
        Assert.True(_service.SessionExists("myserver"));

        var sessions = _service.GetAllSessions();
        Assert.Single(sessions);

        var loaded = sessions[0];
        Assert.Equal("myserver", loaded.EncodedName);
        Assert.Equal("example.com", loaded.HostName);
        Assert.Equal(22, loaded.Port);
        Assert.Equal("ssh", loaded.Protocol);
    }

    [Fact]
    public void WriteSession_CreatesFile()
    {
        var session = new PuttySession
        {
            EncodedName = "test-session",
            Values =
            [
                new RegistrySettingValue { Name = "HostName", Kind = RegistryValueKind.String, Value = "host.com" }
            ]
        };

        _service.WriteSession(session);
        Assert.True(File.Exists(Path.Combine(_sessionsDir, "test-session")));
    }

    [Fact]
    public void SessionExists_ReturnsFalse_WhenMissing()
    {
        Assert.False(_service.SessionExists("nonexistent"));
    }

    [Fact]
    public void DeleteSession_RemovesFile()
    {
        var session = new PuttySession
        {
            EncodedName = "to-delete",
            Values =
            [
                new RegistrySettingValue { Name = "HostName", Kind = RegistryValueKind.String, Value = "host.com" }
            ]
        };

        _service.WriteSession(session);
        Assert.True(_service.SessionExists("to-delete"));

        _service.DeleteSession("to-delete");
        Assert.False(_service.SessionExists("to-delete"));
    }

    [Fact]
    public void DeleteSession_NoError_WhenMissing()
    {
        _service.DeleteSession("nonexistent"); // should not throw
    }

    [Fact]
    public void ReadSession_ParsesHexValues()
    {
        File.WriteAllText(Path.Combine(_sessionsDir, "hex-test"), "SomeValue=0xFF\nOther=hello\n");

        var sessions = _service.GetAllSessions();
        Assert.Single(sessions);

        var hexVal = sessions[0].Values.First(v => v.Name == "SomeValue");
        Assert.Equal(RegistryValueKind.DWord, hexVal.Kind);
        Assert.Equal(255, hexVal.Value);

        var strVal = sessions[0].Values.First(v => v.Name == "Other");
        Assert.Equal(RegistryValueKind.String, strVal.Kind);
        Assert.Equal("hello", strVal.Value);
    }

    [Fact]
    public void ReadSession_SkipsInvalidLines()
    {
        File.WriteAllText(Path.Combine(_sessionsDir, "bad-lines"),
            "Valid=value\nno-equals-sign\nAlsoValid=42\n");

        var sessions = _service.GetAllSessions();
        Assert.Single(sessions);
        Assert.Equal(2, sessions[0].Values.Count);
    }

    [Fact]
    public void ReadSession_SkipsEmptyFile()
    {
        File.WriteAllText(Path.Combine(_sessionsDir, "empty"), "");

        var sessions = _service.GetAllSessions();
        Assert.Empty(sessions); // empty files return null
    }

    [Fact]
    public void GetAllSessions_EmptyNonexistentDir()
    {
        var svc = new UnixSessionService(Path.Combine(_sessionsDir, "nonexistent"));
        var sessions = svc.GetAllSessions();
        Assert.Empty(sessions);
    }

    [Fact]
    public void WriteSession_OverwritesExisting()
    {
        var session1 = new PuttySession
        {
            EncodedName = "overwrite",
            Values =
            [
                new RegistrySettingValue { Name = "HostName", Kind = RegistryValueKind.String, Value = "old.com" }
            ]
        };

        _service.WriteSession(session1);

        var session2 = new PuttySession
        {
            EncodedName = "overwrite",
            Values =
            [
                new RegistrySettingValue { Name = "HostName", Kind = RegistryValueKind.String, Value = "new.com" }
            ]
        };

        _service.WriteSession(session2);

        var sessions = _service.GetAllSessions();
        Assert.Single(sessions);
        Assert.Equal("new.com", sessions[0].HostName);
    }
}
