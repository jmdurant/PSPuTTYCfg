using Microsoft.Win32;
using PuTTYProfileManager.Core.Models;
using PuTTYProfileManager.Core.Services;

namespace PuTTYProfileManager.Core.Tests;

public class LinkedFileServiceTests
{
    [Theory]
    [InlineData("PublicKeyFile", true)]
    [InlineData("DetachedCertificate", true)]
    [InlineData("GSSCustom", true)]
    [InlineData("X11AuthFile", true)]
    [InlineData("BellWaveFile", true)]
    [InlineData("HostName", false)]
    [InlineData("Protocol", false)]
    [InlineData("publickeyfile", true)] // case-insensitive
    public void IsFilePathSetting_IdentifiesCorrectSettings(string name, bool expected)
    {
        Assert.Equal(expected, LinkedFileService.IsFilePathSetting(name));
    }

    [Fact]
    public void GetZipEntryName_StartsWithFiles()
    {
        var result = LinkedFileService.GetZipEntryName("/home/user/.ssh/key.ppk");
        Assert.StartsWith("files/", result);
        Assert.EndsWith("_key.ppk", result);
    }

    [Fact]
    public void GetZipEntryName_DifferentDirectories_ProduceDifferentNames()
    {
        var name1 = LinkedFileService.GetZipEntryName("/home/user/.ssh/key.ppk");
        var name2 = LinkedFileService.GetZipEntryName("/home/other/.ssh/key.ppk");
        Assert.NotEqual(name1, name2);
    }

    [Fact]
    public void GetLinkedFiles_SkipsEmptyPaths()
    {
        var sessions = new List<PuttySession>
        {
            new()
            {
                EncodedName = "test",
                Values =
                [
                    new RegistrySettingValue { Name = "PublicKeyFile", Kind = RegistryValueKind.String, Value = "" },
                    new RegistrySettingValue { Name = "HostName", Kind = RegistryValueKind.String, Value = "host.com" }
                ]
            }
        };

        var files = LinkedFileService.GetLinkedFiles(sessions);
        Assert.Empty(files);
    }

    [Fact]
    public void GetLinkedFiles_DeduplicatesSamePath()
    {
        var sessions = new List<PuttySession>
        {
            new()
            {
                EncodedName = "s1",
                Values = [new RegistrySettingValue { Name = "PublicKeyFile", Kind = RegistryValueKind.String, Value = "/tmp/nonexistent_test_key.ppk" }]
            },
            new()
            {
                EncodedName = "s2",
                Values = [new RegistrySettingValue { Name = "PublicKeyFile", Kind = RegistryValueKind.String, Value = "/tmp/nonexistent_test_key.ppk" }]
            }
        };

        var files = LinkedFileService.GetLinkedFiles(sessions);
        Assert.Single(files);
    }

    [Fact]
    public void UpdateSessionPaths_UpdatesMappedPaths()
    {
        var session = new PuttySession
        {
            EncodedName = "test",
            Values =
            [
                new RegistrySettingValue { Name = "PublicKeyFile", Kind = RegistryValueKind.String, Value = "/old/path/key.ppk" },
                new RegistrySettingValue { Name = "HostName", Kind = RegistryValueKind.String, Value = "host.com" }
            ]
        };

        var mapping = new Dictionary<string, string>
        {
            ["/old/path/key.ppk"] = "files/abcdef01_key.ppk"
        };

        LinkedFileService.UpdateSessionPaths(session, "/new/dir", mapping);

        var keyValue = session.Values.First(v => v.Name == "PublicKeyFile");
        Assert.Contains("key.ppk", keyValue.Value?.ToString());
        Assert.StartsWith("/new/dir", keyValue.Value?.ToString()!);
        // Should strip the hash prefix, resulting in just "key.ppk"
        Assert.Equal(Path.Combine("/new/dir", "key.ppk"), keyValue.Value?.ToString());
    }

    [Fact]
    public void UpdateSessionPaths_DoesNotChangeUnmappedPaths()
    {
        var session = new PuttySession
        {
            EncodedName = "test",
            Values =
            [
                new RegistrySettingValue { Name = "PublicKeyFile", Kind = RegistryValueKind.String, Value = "/unmapped/key.ppk" }
            ]
        };

        var mapping = new Dictionary<string, string>();
        LinkedFileService.UpdateSessionPaths(session, "/new/dir", mapping);

        Assert.Equal("/unmapped/key.ppk", session.Values[0].Value?.ToString());
    }
}
