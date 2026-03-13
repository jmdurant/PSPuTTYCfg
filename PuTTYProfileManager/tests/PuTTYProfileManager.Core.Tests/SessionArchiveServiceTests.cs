using Microsoft.Win32;
using PuTTYProfileManager.Core.Models;
using PuTTYProfileManager.Core.Services;

namespace PuTTYProfileManager.Core.Tests;

public class SessionArchiveServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SessionArchiveService _service;

    public SessionArchiveServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"puttymgr_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _service = new SessionArchiveService();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private static PuttySession CreateTestSession(string name = "TestSession", string host = "example.com", int port = 22)
    {
        return new PuttySession
        {
            EncodedName = name,
            Values =
            [
                new RegistrySettingValue { Name = "HostName", Kind = RegistryValueKind.String, Value = host },
                new RegistrySettingValue { Name = "PortNumber", Kind = RegistryValueKind.DWord, Value = port },
                new RegistrySettingValue { Name = "Protocol", Kind = RegistryValueKind.String, Value = "ssh" }
            ]
        };
    }

    [Fact]
    public void ExportImport_RoundTrip_PreservesSession()
    {
        var zipPath = Path.Combine(_tempDir, "test.zip");
        var session = CreateTestSession();

        _service.ExportToZip(zipPath, [session], includeLinkedFiles: false);

        Assert.True(File.Exists(zipPath));

        var contents = _service.ImportFromZip(zipPath);
        Assert.Single(contents.Sessions);
        Assert.Equal("TestSession", contents.Sessions[0].EncodedName);
        Assert.Equal("example.com", contents.Sessions[0].HostName);
        Assert.Equal(22, contents.Sessions[0].Port);
        Assert.Equal("ssh", contents.Sessions[0].Protocol);
    }

    [Fact]
    public void ExportImport_MultipleSessions()
    {
        var zipPath = Path.Combine(_tempDir, "multi.zip");
        var sessions = new List<PuttySession>
        {
            CreateTestSession("prod-web", "web.example.com", 22),
            CreateTestSession("prod-db", "db.example.com", 5432),
            CreateTestSession("dev-server", "dev.example.com", 2222)
        };

        _service.ExportToZip(zipPath, sessions, includeLinkedFiles: false);
        var contents = _service.ImportFromZip(zipPath);

        Assert.Equal(3, contents.Sessions.Count);
        Assert.Contains(contents.Sessions, s => s.EncodedName == "prod-web");
        Assert.Contains(contents.Sessions, s => s.EncodedName == "prod-db");
        Assert.Contains(contents.Sessions, s => s.EncodedName == "dev-server");
    }

    [Fact]
    public void ExportImport_WithPassword_IsMarkedProtected()
    {
        var zipPath = Path.Combine(_tempDir, "encrypted.zip");
        var session = CreateTestSession();

        _service.ExportToZip(zipPath, [session], includeLinkedFiles: false, password: "secret123");
        Assert.True(_service.IsPasswordProtected(zipPath));
    }

    [Fact]
    public void ExportImport_WithPassword_RoundTrip()
    {
        var zipPath = Path.Combine(_tempDir, "encrypted_rt.zip");
        var session = CreateTestSession("EncryptedSession", "secure.example.com", 443);

        _service.ExportToZip(zipPath, [session], includeLinkedFiles: false, password: "myP@ssw0rd!");

        var contents = _service.ImportFromZip(zipPath, password: "myP@ssw0rd!");
        Assert.Single(contents.Sessions);
        Assert.Equal("EncryptedSession", contents.Sessions[0].EncodedName);
        Assert.Equal("secure.example.com", contents.Sessions[0].HostName);
        Assert.Equal(443, contents.Sessions[0].Port);
        Assert.False(contents.HasErrors);
    }

    [Fact]
    public void ExtractLinkedFiles_WithPassword()
    {
        var zipPath = Path.Combine(_tempDir, "encrypted_files.zip");
        var keyPath = Path.Combine(_tempDir, "secret_key.ppk");
        File.WriteAllText(keyPath, "encrypted-key-content");

        var session = new PuttySession
        {
            EncodedName = "EncryptedWithKey",
            Values =
            [
                new RegistrySettingValue { Name = "PublicKeyFile", Kind = RegistryValueKind.String, Value = keyPath }
            ]
        };

        _service.ExportToZip(zipPath, [session], includeLinkedFiles: true, password: "filepass");

        var extractDir = Path.Combine(_tempDir, "encrypted_extracted");
        _service.ExtractLinkedFiles(zipPath, extractDir, password: "filepass");

        Assert.True(File.Exists(Path.Combine(extractDir, "secret_key.ppk")));
        Assert.Equal("encrypted-key-content", File.ReadAllText(Path.Combine(extractDir, "secret_key.ppk")));
    }

    [Fact]
    public void IsPasswordProtected_ReturnsFalse_ForUnencrypted()
    {
        var zipPath = Path.Combine(_tempDir, "plain.zip");
        _service.ExportToZip(zipPath, [CreateTestSession()], includeLinkedFiles: false);

        Assert.False(_service.IsPasswordProtected(zipPath));
    }

    [Fact]
    public void IsPasswordProtected_ReturnsFalse_ForMissingFile()
    {
        Assert.False(_service.IsPasswordProtected(Path.Combine(_tempDir, "missing.zip")));
    }

    [Fact]
    public void ImportFromZip_ThrowsFileNotFound_ForMissingFile()
    {
        Assert.Throws<FileNotFoundException>(() =>
            _service.ImportFromZip(Path.Combine(_tempDir, "missing.zip")));
    }

    [Fact]
    public void ExportImport_PreservesBinaryValues()
    {
        var zipPath = Path.Combine(_tempDir, "binary.zip");
        var session = new PuttySession
        {
            EncodedName = "BinaryTest",
            Values =
            [
                new RegistrySettingValue
                {
                    Name = "SomeBlob",
                    Kind = RegistryValueKind.Binary,
                    Value = new byte[] { 0x01, 0x02, 0xFF, 0xFE }
                }
            ]
        };

        _service.ExportToZip(zipPath, [session], includeLinkedFiles: false);
        var contents = _service.ImportFromZip(zipPath);

        Assert.Single(contents.Sessions);
        var blob = contents.Sessions[0].Values.First(v => v.Name == "SomeBlob");
        Assert.Equal(RegistryValueKind.Binary, blob.Kind);
        Assert.Equal(new byte[] { 0x01, 0x02, 0xFF, 0xFE }, (byte[])blob.Value!);
    }

    [Fact]
    public void ExportImport_PreservesMultiStringValues()
    {
        var zipPath = Path.Combine(_tempDir, "multi_string.zip");
        var session = new PuttySession
        {
            EncodedName = "MultiStringTest",
            Values =
            [
                new RegistrySettingValue
                {
                    Name = "SomeMulti",
                    Kind = RegistryValueKind.MultiString,
                    Value = new[] { "line1", "line2", "line3" }
                }
            ]
        };

        _service.ExportToZip(zipPath, [session], includeLinkedFiles: false);
        var contents = _service.ImportFromZip(zipPath);

        var multi = contents.Sessions[0].Values.First(v => v.Name == "SomeMulti");
        Assert.Equal(RegistryValueKind.MultiString, multi.Kind);
        Assert.Equal(new[] { "line1", "line2", "line3" }, (string[])multi.Value!);
    }

    [Fact]
    public void ExportImport_WithLinkedFiles()
    {
        var zipPath = Path.Combine(_tempDir, "with_files.zip");
        var keyPath = Path.Combine(_tempDir, "test_key.ppk");
        File.WriteAllText(keyPath, "PuTTY-User-Key-File-3: ssh-ed25519");

        var session = new PuttySession
        {
            EncodedName = "WithKey",
            Values =
            [
                new RegistrySettingValue { Name = "HostName", Kind = RegistryValueKind.String, Value = "host.com" },
                new RegistrySettingValue { Name = "PublicKeyFile", Kind = RegistryValueKind.String, Value = keyPath }
            ]
        };

        _service.ExportToZip(zipPath, [session], includeLinkedFiles: true);
        var contents = _service.ImportFromZip(zipPath);

        Assert.Single(contents.Sessions);
        Assert.NotEmpty(contents.LinkedFileEntries);
        Assert.NotEmpty(contents.FileMapping);
    }

    [Fact]
    public void ExtractLinkedFiles_StripHashPrefix()
    {
        var zipPath = Path.Combine(_tempDir, "extract_test.zip");
        var keyPath = Path.Combine(_tempDir, "my_key.ppk");
        File.WriteAllText(keyPath, "key-content");

        var session = new PuttySession
        {
            EncodedName = "Extract",
            Values =
            [
                new RegistrySettingValue { Name = "PublicKeyFile", Kind = RegistryValueKind.String, Value = keyPath }
            ]
        };

        _service.ExportToZip(zipPath, [session], includeLinkedFiles: true);

        var extractDir = Path.Combine(_tempDir, "extracted");
        _service.ExtractLinkedFiles(zipPath, extractDir);

        // Should extract with friendly name (no hash prefix)
        Assert.True(File.Exists(Path.Combine(extractDir, "my_key.ppk")),
            "Expected file to be extracted with original name (hash prefix stripped)");
        Assert.Equal("key-content", File.ReadAllText(Path.Combine(extractDir, "my_key.ppk")));
    }

    [Fact]
    public void ExportImport_EmptySessionList()
    {
        var zipPath = Path.Combine(_tempDir, "empty.zip");
        _service.ExportToZip(zipPath, [], includeLinkedFiles: false);

        var contents = _service.ImportFromZip(zipPath);
        Assert.Empty(contents.Sessions);
        Assert.False(contents.HasErrors);
    }

    [Fact]
    public void ExportImport_PreservesDWordValues()
    {
        var zipPath = Path.Combine(_tempDir, "dword.zip");
        var session = new PuttySession
        {
            EncodedName = "DWordTest",
            Values =
            [
                new RegistrySettingValue { Name = "PortNumber", Kind = RegistryValueKind.DWord, Value = 443 },
                new RegistrySettingValue { Name = "BigNumber", Kind = RegistryValueKind.QWord, Value = 9999999999L }
            ]
        };

        _service.ExportToZip(zipPath, [session], includeLinkedFiles: false);
        var contents = _service.ImportFromZip(zipPath);

        var port = contents.Sessions[0].Values.First(v => v.Name == "PortNumber");
        Assert.Equal(RegistryValueKind.DWord, port.Kind);
        Assert.Equal(443, port.Value);

        var big = contents.Sessions[0].Values.First(v => v.Name == "BigNumber");
        Assert.Equal(RegistryValueKind.QWord, big.Kind);
        Assert.Equal(9999999999L, big.Value);
    }
}
