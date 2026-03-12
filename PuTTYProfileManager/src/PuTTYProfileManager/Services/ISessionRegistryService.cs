using PuTTYProfileManager.Models;

namespace PuTTYProfileManager.Services;

public interface ISessionRegistryService
{
    List<PuttySession> GetAllSessions();
    bool SessionExists(string encodedName);
    void WriteSession(PuttySession session);
    void DeleteSession(string encodedName);
}
