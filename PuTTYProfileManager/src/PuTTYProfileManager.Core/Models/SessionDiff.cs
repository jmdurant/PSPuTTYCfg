namespace PuTTYProfileManager.Core.Models;

public class SessionDiff
{
    public PuttySession LocalSession { get; init; } = null!;
    public PuttySession ArchiveSession { get; init; } = null!;

    public string DisplayName => LocalSession.DisplayName;

    public DateTime? LocalLastModified { get; init; }
    public DateTime? ArchiveDate { get; init; }

    public List<SettingDiff> ChangedSettings { get; init; } = [];
    public List<RegistrySettingValue> AddedInArchive { get; init; } = [];
    public List<RegistrySettingValue> RemovedInArchive { get; init; } = [];

    public bool HasDifferences =>
        ChangedSettings.Count > 0 || AddedInArchive.Count > 0 || RemovedInArchive.Count > 0;

    public int TotalDifferences =>
        ChangedSettings.Count + AddedInArchive.Count + RemovedInArchive.Count;
}

public class SettingDiff
{
    public string Name { get; init; } = string.Empty;
    public string? LocalValue { get; init; }
    public string? ArchiveValue { get; init; }
}
