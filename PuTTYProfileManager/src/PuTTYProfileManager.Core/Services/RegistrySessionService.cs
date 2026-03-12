using System.Runtime.InteropServices;
using Microsoft.Win32;
using PuTTYProfileManager.Core.Models;

namespace PuTTYProfileManager.Core.Services;

public class RegistrySessionService : ISessionService
{
    private const string PuttySessionsPath = @"SOFTWARE\SimonTatham\PuTTY\Sessions";

    public RegistrySessionService()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new PlatformNotSupportedException("Registry-based sessions are only available on Windows.");
    }

    public List<PuttySession> GetAllSessions()
    {
        var sessions = new List<PuttySession>();

        using var sessionsKey = Registry.CurrentUser.OpenSubKey(PuttySessionsPath);
        if (sessionsKey is null)
            return sessions;

        foreach (var subKeyName in sessionsKey.GetSubKeyNames())
        {
            var session = ReadSession(sessionsKey, subKeyName);
            if (session is not null)
                sessions.Add(session);
        }

        return sessions;
    }

    public bool SessionExists(string encodedName)
    {
        using var sessionsKey = Registry.CurrentUser.OpenSubKey(PuttySessionsPath);
        if (sessionsKey is null)
            return false;

        using var sessionKey = sessionsKey.OpenSubKey(encodedName);
        return sessionKey is not null;
    }

    public void WriteSession(PuttySession session)
    {
        var path = $@"{PuttySessionsPath}\{session.EncodedName}";
        using var key = Registry.CurrentUser.CreateSubKey(path, true);

        foreach (var val in session.Values)
        {
            key.SetValue(val.Name, val.Value ?? string.Empty, val.Kind);
        }
    }

    public void DeleteSession(string encodedName)
    {
        using var sessionsKey = Registry.CurrentUser.OpenSubKey(PuttySessionsPath, true);
        sessionsKey?.DeleteSubKeyTree(encodedName, false);
    }

    private static PuttySession? ReadSession(RegistryKey parentKey, string encodedName)
    {
        using var sessionKey = parentKey.OpenSubKey(encodedName);
        if (sessionKey is null)
            return null;

        var session = new PuttySession { EncodedName = encodedName };

        foreach (var valueName in sessionKey.GetValueNames())
        {
            var kind = sessionKey.GetValueKind(valueName);
            var value = sessionKey.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);

            session.Values.Add(new RegistrySettingValue
            {
                Name = valueName,
                Kind = kind,
                Value = value
            });
        }

        return session;
    }
}
