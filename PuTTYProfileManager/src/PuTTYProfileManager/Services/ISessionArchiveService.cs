using PuTTYProfileManager.Models;

namespace PuTTYProfileManager.Services;

public interface ISessionArchiveService
{
    void ExportToZip(string zipPath, IEnumerable<PuttySession> sessions, string? password = null);
    List<PuttySession> ImportFromZip(string zipPath, string? password = null);
    bool IsPasswordProtected(string zipPath);
}
