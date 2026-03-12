using PuTTYProfileManager.Core.Models;

namespace PuTTYProfileManager.Core.Services;

public interface ISessionService
{
    List<PuttySession> GetAllSessions();
    bool SessionExists(string encodedName);
    void WriteSession(PuttySession session);
    void DeleteSession(string encodedName);
}
