using Microsoft.Win32;
using PuTTYProfileManager.Core.Models;
using PuTTYProfileManager.Core.Services;

namespace PuTTYProfileManager.Core.Tests;

public class SshConfigSessionServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _configPath;

    public SshConfigSessionServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"sshconfig_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _configPath = Path.Combine(_tempDir, "config");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private SshConfigSessionService CreateService() => new(_configPath);

    [Fact]
    public void GetAllSessions_NoFile_ReturnsEmpty()
    {
        var svc = CreateService();
        Assert.Empty(svc.GetAllSessions());
    }

    [Fact]
    public void GetAllSessions_ParsesBasicConfig()
    {
        File.WriteAllText(_configPath, """
            Host myserver
                HostName 192.168.1.100
                Port 2222
                User admin
                IdentityFile ~/.ssh/id_rsa
            """);

        var svc = CreateService();
        var sessions = svc.GetAllSessions();

        Assert.Single(sessions);
        var s = sessions[0];
        Assert.Equal("myserver", s.DisplayName);
        Assert.Equal("192.168.1.100", s.HostName);
        Assert.Equal(2222, s.Port);

        var user = s.Values.First(v => v.Name == "UserName");
        Assert.Equal("admin", user.Value);

        var key = s.Values.First(v => v.Name == "PublicKeyFile");
        Assert.Equal("~/.ssh/id_rsa", key.Value);
    }

    [Fact]
    public void GetAllSessions_MultipleSessions()
    {
        File.WriteAllText(_configPath, """
            Host web
                HostName web.example.com
                User deploy

            Host db
                HostName db.example.com
                Port 5432
                User postgres
            """);

        var svc = CreateService();
        var sessions = svc.GetAllSessions();

        Assert.Equal(2, sessions.Count);
        Assert.Contains(sessions, s => s.DisplayName == "web");
        Assert.Contains(sessions, s => s.DisplayName == "db");
    }

    [Fact]
    public void GetAllSessions_SkipsWildcardHosts()
    {
        File.WriteAllText(_configPath, """
            Host *
                ServerAliveInterval 60

            Host myserver
                HostName 10.0.0.1
            """);

        var svc = CreateService();
        var sessions = svc.GetAllSessions();

        Assert.Single(sessions);
        Assert.Equal("myserver", sessions[0].DisplayName);
    }

    [Fact]
    public void GetAllSessions_SkipsComments()
    {
        File.WriteAllText(_configPath, """
            # This is a comment
            Host myserver
                # Another comment
                HostName 10.0.0.1
                Port 22
            """);

        var svc = CreateService();
        var sessions = svc.GetAllSessions();

        Assert.Single(sessions);
        Assert.Equal("10.0.0.1", sessions[0].HostName);
    }

    [Fact]
    public void GetAllSessions_ParsesBooleanValues()
    {
        File.WriteAllText(_configPath, """
            Host myserver
                HostName 10.0.0.1
                ForwardAgent yes
                Compression no
            """);

        var svc = CreateService();
        var sessions = svc.GetAllSessions();
        var s = sessions[0];

        var fwdAgent = s.Values.First(v => v.Name == "AgentFwd");
        Assert.Equal(RegistryValueKind.DWord, fwdAgent.Kind);
        Assert.Equal(1, fwdAgent.Value);

        var compression = s.Values.First(v => v.Name == "Compression");
        Assert.Equal(RegistryValueKind.DWord, compression.Kind);
        Assert.Equal(0, compression.Value);
    }

    [Fact]
    public void WriteSession_CreatesValidConfig()
    {
        var svc = CreateService();

        var session = new PuttySession
        {
            EncodedName = "myserver",
            Values =
            [
                new RegistrySettingValue { Name = "HostName", Kind = RegistryValueKind.String, Value = "192.168.1.50" },
                new RegistrySettingValue { Name = "PortNumber", Kind = RegistryValueKind.DWord, Value = 2222 },
                new RegistrySettingValue { Name = "UserName", Kind = RegistryValueKind.String, Value = "pi" },
                new RegistrySettingValue { Name = "PublicKeyFile", Kind = RegistryValueKind.String, Value = "~/.ssh/id_ed25519" },
            ]
        };

        svc.WriteSession(session);

        Assert.True(File.Exists(_configPath));
        var content = File.ReadAllText(_configPath);
        Assert.Contains("Host myserver", content);
        Assert.Contains("HostName 192.168.1.50", content);
        Assert.Contains("Port 2222", content);
        Assert.Contains("User pi", content);
        Assert.Contains("IdentityFile ~/.ssh/id_ed25519", content);
    }

    [Fact]
    public void WriteSession_SkipsDefaultPort()
    {
        var svc = CreateService();

        var session = new PuttySession
        {
            EncodedName = "myserver",
            Values =
            [
                new RegistrySettingValue { Name = "HostName", Kind = RegistryValueKind.String, Value = "host.com" },
                new RegistrySettingValue { Name = "PortNumber", Kind = RegistryValueKind.DWord, Value = 22 },
            ]
        };

        svc.WriteSession(session);

        var content = File.ReadAllText(_configPath);
        Assert.DoesNotContain("Port", content);
    }

    [Fact]
    public void WriteSession_AppendsToExisting()
    {
        File.WriteAllText(_configPath, """
            Host existing
                HostName old.com

            """);

        var svc = CreateService();
        svc.WriteSession(new PuttySession
        {
            EncodedName = "newserver",
            Values =
            [
                new RegistrySettingValue { Name = "HostName", Kind = RegistryValueKind.String, Value = "new.com" },
            ]
        });

        var sessions = svc.GetAllSessions();
        Assert.Equal(2, sessions.Count);
        Assert.Contains(sessions, s => s.DisplayName == "existing");
        Assert.Contains(sessions, s => s.DisplayName == "newserver");
    }

    [Fact]
    public void WriteSession_OverwritesExistingBlock()
    {
        var svc = CreateService();

        svc.WriteSession(new PuttySession
        {
            EncodedName = "myserver",
            Values =
            [
                new RegistrySettingValue { Name = "HostName", Kind = RegistryValueKind.String, Value = "old.com" },
            ]
        });

        svc.WriteSession(new PuttySession
        {
            EncodedName = "myserver",
            Values =
            [
                new RegistrySettingValue { Name = "HostName", Kind = RegistryValueKind.String, Value = "new.com" },
            ]
        });

        var sessions = svc.GetAllSessions();
        Assert.Single(sessions);
        Assert.Equal("new.com", sessions[0].HostName);
    }

    [Fact]
    public void RoundTrip_WriteAndRead()
    {
        var svc = CreateService();

        var original = new PuttySession
        {
            EncodedName = "RPI5",
            Values =
            [
                new RegistrySettingValue { Name = "HostName", Kind = RegistryValueKind.String, Value = "raspberrypi.local" },
                new RegistrySettingValue { Name = "PortNumber", Kind = RegistryValueKind.DWord, Value = 22 },
                new RegistrySettingValue { Name = "UserName", Kind = RegistryValueKind.String, Value = "pi" },
                new RegistrySettingValue { Name = "PublicKeyFile", Kind = RegistryValueKind.String, Value = "~/.ssh/pi_key" },
                new RegistrySettingValue { Name = "AgentFwd", Kind = RegistryValueKind.DWord, Value = 1 },
            ]
        };

        svc.WriteSession(original);

        var loaded = svc.GetSession(Uri.EscapeDataString("RPI5"));
        Assert.NotNull(loaded);
        Assert.Equal("raspberrypi.local", loaded.HostName);
        Assert.Equal("pi", loaded.Values.First(v => v.Name == "UserName").Value);
        Assert.Equal("~/.ssh/pi_key", loaded.Values.First(v => v.Name == "PublicKeyFile").Value);
    }

    [Fact]
    public void DeleteSession_RemovesBlock()
    {
        File.WriteAllText(_configPath, """
            Host keep
                HostName keep.com

            Host remove
                HostName remove.com
                Port 2222

            Host alsokeep
                HostName alsokeep.com
            """);

        var svc = CreateService();
        svc.DeleteSession(Uri.EscapeDataString("remove"));

        var sessions = svc.GetAllSessions();
        Assert.Equal(2, sessions.Count);
        Assert.Contains(sessions, s => s.DisplayName == "keep");
        Assert.Contains(sessions, s => s.DisplayName == "alsokeep");
        Assert.DoesNotContain(sessions, s => s.DisplayName == "remove");
    }

    [Fact]
    public void SessionExists_ReturnsFalse_WhenMissing()
    {
        var svc = CreateService();
        Assert.False(svc.SessionExists("nonexistent"));
    }

    [Fact]
    public void SessionExists_ReturnsTrue_WhenPresent()
    {
        File.WriteAllText(_configPath, """
            Host myserver
                HostName host.com
            """);

        var svc = CreateService();
        Assert.True(svc.SessionExists(Uri.EscapeDataString("myserver")));
    }

    [Fact]
    public void GetAllSessions_HandlesEqualsSignSyntax()
    {
        // SSH config allows "Keyword=value" in addition to "Keyword value"
        File.WriteAllText(_configPath, """
            Host myserver
                HostName=10.0.0.1
                Port=2222
            """);

        var svc = CreateService();
        var sessions = svc.GetAllSessions();

        Assert.Single(sessions);
        Assert.Equal("10.0.0.1", sessions[0].HostName);
        Assert.Equal(2222, sessions[0].Port);
    }
}
