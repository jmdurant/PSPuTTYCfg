using CommunityToolkit.Mvvm.ComponentModel;
using PuTTYProfileManager.Core.Models;

namespace PuTTYProfileManager.Models;

public partial class SelectableSession : ObservableObject
{
    public PuttySession Session { get; }

    [ObservableProperty]
    private bool _isSelected;

    public SelectableSession(PuttySession session, bool isSelected = false)
    {
        Session = session;
        _isSelected = isSelected;
    }

    public string DisplayName => Session.DisplayName;
    public string Summary => Session.Summary;
    public string EncodedName => Session.EncodedName;
    public int SettingsCount => Session.Values.Count;
}
