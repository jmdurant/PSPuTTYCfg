using System.Diagnostics;

namespace PuTTYProfileManager.Core.Services;

public static class WslEnvironment
{
    private static bool? _isWsl;
    private static string? _windowsUserProfile;

    public static bool IsWsl
    {
        get
        {
            _isWsl ??= DetectWsl();
            return _isWsl.Value;
        }
    }

    public static string? WindowsUserProfile
    {
        get
        {
            if (!IsWsl) return null;
            _windowsUserProfile ??= DetectWindowsUserProfile();
            return _windowsUserProfile;
        }
    }

    public static string RegExePath => "/mnt/c/Windows/System32/reg.exe";

    private static bool DetectWsl()
    {
        try
        {
            if (!File.Exists("/proc/version"))
                return false;

            var version = File.ReadAllText("/proc/version");
            return version.Contains("microsoft", StringComparison.OrdinalIgnoreCase)
                || version.Contains("WSL", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string? DetectWindowsUserProfile()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "/mnt/c/Windows/System32/cmd.exe",
                Arguments = "/c echo %USERPROFILE%",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null) return null;

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5000);

            // Convert Windows path (C:\Users\user) to WSL path (/mnt/c/Users/user)
            return ConvertWindowsToWslPath(output);
        }
        catch
        {
            return null;
        }
    }

    public static string ConvertWindowsToWslPath(string windowsPath)
    {
        if (windowsPath.Length >= 2 && windowsPath[1] == ':')
        {
            var drive = char.ToLower(windowsPath[0]);
            var rest = windowsPath[2..].Replace('\\', '/').TrimEnd('\r', '\n');
            return $"/mnt/{drive}{rest}";
        }

        return windowsPath;
    }
}
