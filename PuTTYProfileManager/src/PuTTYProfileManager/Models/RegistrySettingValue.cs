using Microsoft.Win32;

namespace PuTTYProfileManager.Models;

public class RegistrySettingValue
{
    public string Name { get; set; } = string.Empty;
    public RegistryValueKind Kind { get; set; }
    public object? Value { get; set; }
}
