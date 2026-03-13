using PuTTYProfileManager.Core.Models;

namespace PuTTYProfileManager.Core.Services;

public interface ISessionService
{
    List<PuttySession> GetAllSessions();
    PuttySession? GetSession(string encodedName);
    bool SessionExists(string encodedName);
    DateTime? GetSessionLastModified(string encodedName);
    void WriteSession(PuttySession session);
    void DeleteSession(string encodedName);
}
