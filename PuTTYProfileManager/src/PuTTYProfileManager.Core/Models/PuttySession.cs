namespace PuTTYProfileManager.Core.Models;

public class PuttySession
{
    public string EncodedName { get; set; } = string.Empty;

    public string DisplayName =>
        Uri.UnescapeDataString(EncodedName);

    public List<RegistrySettingValue> Values { get; set; } = [];

    public string? HostName =>
        Values.FirstOrDefault(v =>
            v.Name.Equals("HostName", StringComparison.OrdinalIgnoreCase))?.Value?.ToString();

    public string? Protocol =>
        Values.FirstOrDefault(v =>
            v.Name.Equals("Protocol", StringComparison.OrdinalIgnoreCase))?.Value?.ToString();

    public int? Port
    {
        get
        {
            var val = Values.FirstOrDefault(v =>
                v.Name.Equals("PortNumber", StringComparison.OrdinalIgnoreCase))?.Value;
            return val is int i ? i : null;
        }
    }

    public string Summary
    {
        get
        {
            var proto = Protocol ?? "ssh";
            var host = HostName;
            var port = Port;
            if (string.IsNullOrEmpty(host))
                return "(no host configured)";
            return port.HasValue ? $"{proto}://{host}:{port}" : $"{proto}://{host}";
        }
    }
}
