using Avalonia.Threading;
using CraftHub.Models;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace CraftHub.Services;

public sealed class NotificationService
{
    private const int MaxActivePopups = 5;
    private readonly ObservableCollection<Notification> _history = new();
    private readonly ObservableCollection<Notification> _active = new();
    private bool _showPopups = true;
    private bool _popupsSuppressed;

    public NotificationService()
    {
        Notifications = new ReadOnlyObservableCollection<Notification>(_history);
        ActiveNotifications = new ReadOnlyObservableCollection<Notification>(_active);
    }

    public ReadOnlyObservableCollection<Notification> Notifications { get; }
    public ReadOnlyObservableCollection<Notification> ActiveNotifications { get; }

    public bool ShowPopups
    {
        get => _showPopups;
        set
        {
            if (_showPopups == value)
            {
                return;
            }
            _showPopups = value;
            if (!_showPopups)
            {
                _active.Clear();
            }
        }
    }

    // Used to temporarily hide/suppress popups without changing the user's ShowPopups preference.
    public bool PopupsSuppressed
    {
        get => _popupsSuppressed;
        set
        {
            if (_popupsSuppressed == value)
            {
                return;
            }
            _popupsSuppressed = value;
            if (_popupsSuppressed)
            {
                _active.Clear();
            }
        }
    }

    public void Publish(NotificationType type, string message)
    {
        var notification = new Notification(message, type);
        _history.Add(notification);
        if (!ShowPopups || PopupsSuppressed)
        {
            return;
        }

        _active.Add(notification);
        TrimActivePopups();
        _ = AutoDismissAsync(notification);
    }

    public void Dismiss(Notification notification)
    {
        _active.Remove(notification);
    }

    public void DismissHistory(Notification notification)
    {
        _history.Remove(notification);
        _active.Remove(notification);
    }

    public void ClearHistory()
    {
        _history.Clear();
        _active.Clear();
    }

    private async Task AutoDismissAsync(Notification notification)
    {
        await Task.Delay(5000);
        Dispatcher.UIThread.Post(() => _active.Remove(notification));
    }

    private void TrimActivePopups()
    {
        while (_active.Count > MaxActivePopups)
        {
            _active.RemoveAt(0);
        }
    }
}
