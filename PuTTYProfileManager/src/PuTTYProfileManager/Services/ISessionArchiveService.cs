using PuTTYProfileManager.Models;

namespace PuTTYProfileManager.Services;

public interface ISessionArchiveService
{
    void ExportToZip(string zipPath, IEnumerable<PuttySession> sessions, bool includeLinkedFiles, string? password = null);
    ArchiveContents ImportFromZip(string zipPath, string? password = null);
    void ExtractLinkedFiles(string zipPath, string destinationFolder, string? password = null);
    bool IsPasswordProtected(string zipPath);
}

public class ArchiveContents
{
    public List<PuttySession> Sessions { get; set; } = [];
    public List<string> LinkedFileEntries { get; set; } = [];
    public Dictionary<string, string> FileMapping { get; set; } = [];
}
