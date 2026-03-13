using Microsoft.Win32;
using PuTTYProfileManager.Core.Models;

namespace PuTTYProfileManager.Core.Tests;

public class PuttySessionTests
{
    [Fact]
    public void DisplayName_DecodesUrlEncodedName()
    {
        var session = new PuttySession { EncodedName = "My%20Server" };
        Assert.Equal("My Server", session.DisplayName);
    }

    [Fact]
    public void DisplayName_PlainNameUnchanged()
    {
        var session = new PuttySession { EncodedName = "production" };
        Assert.Equal("production", session.DisplayName);
    }

    [Fact]
    public void HostName_ReturnsValue_WhenPresent()
    {
        var session = new PuttySession
        {
            EncodedName = "test",
            Values =
            [
                new RegistrySettingValue { Name = "HostName", Kind = RegistryValueKind.String, Value = "example.com" }
            ]
        };
        Assert.Equal("example.com", session.HostName);
    }

    [Fact]
    public void HostName_ReturnsNull_WhenMissing()
    {
        var session = new PuttySession { EncodedName = "test" };
        Assert.Null(session.HostName);
    }

    [Fact]
    public void Port_ReturnsValue_WhenPresent()
    {
        var session = new PuttySession
        {
            EncodedName = "test",
            Values =
            [
                new RegistrySettingValue { Name = "PortNumber", Kind = RegistryValueKind.DWord, Value = 2222 }
            ]
        };
        Assert.Equal(2222, session.Port);
    }

    [Fact]
    public void Protocol_ReturnsValue_WhenPresent()
    {
        var session = new PuttySession
        {
            EncodedName = "test",
            Values =
            [
                new RegistrySettingValue { Name = "Protocol", Kind = RegistryValueKind.String, Value = "telnet" }
            ]
        };
        Assert.Equal("telnet", session.Protocol);
    }

    [Fact]
    public void Summary_ReturnsNoHostConfigured_WhenNoHost()
    {
        var session = new PuttySession { EncodedName = "test" };
        Assert.Equal("(no host configured)", session.Summary);
    }

    [Fact]
    public void Summary_FormatsWithProtocolHostPort()
    {
        var session = new PuttySession
        {
            EncodedName = "test",
            Values =
            [
                new RegistrySettingValue { Name = "HostName", Kind = RegistryValueKind.String, Value = "server.com" },
                new RegistrySettingValue { Name = "Protocol", Kind = RegistryValueKind.String, Value = "ssh" },
                new RegistrySettingValue { Name = "PortNumber", Kind = RegistryValueKind.DWord, Value = 22 }
            ]
        };
        Assert.Equal("ssh://server.com:22", session.Summary);
    }

    [Fact]
    public void Summary_OmitsPort_WhenMissing()
    {
        var session = new PuttySession
        {
            EncodedName = "test",
            Values =
            [
                new RegistrySettingValue { Name = "HostName", Kind = RegistryValueKind.String, Value = "server.com" }
            ]
        };
        Assert.Equal("ssh://server.com", session.Summary);
    }
}
