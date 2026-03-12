using CommunityToolkit.Mvvm.ComponentModel;
using PuTTYProfileManager.Core.Models;

namespace PuTTYProfileManager.Avalonia.Models;

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
}
