namespace PuTTYProfileManager.Models;

public class LinkedFile
{
    public string OriginalPath { get; set; } = string.Empty;
    public string SettingName { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public bool Exists { get; set; }
    public long FileSize { get; set; }

    public string DisplaySize => FileSize switch
    {
        < 1024 => $"{FileSize} B",
        < 1024 * 1024 => $"{FileSize / 1024.0:F1} KB",
        _ => $"{FileSize / (1024.0 * 1024.0):F1} MB"
    };

    public string SettingLabel => SettingName switch
    {
        "PublicKeyFile" => "Private Key (PPK)",
        "DetachedCertificate" => "Certificate",
        "GSSCustom" => "GSSAPI Library",
        "X11AuthFile" => "X11 Auth File",
        "BellWaveFile" => "Bell Sound",
        _ => SettingName
    };
}
