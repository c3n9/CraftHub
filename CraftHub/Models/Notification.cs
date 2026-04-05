using Avalonia.Media;
using System;

namespace CraftHub.Models;

public enum NotificationType
{
    Info,
    Success,
    Warning,
    Error
}

public sealed class Notification
{
    public Notification(string message, NotificationType type)
    {
        Id = Guid.NewGuid().ToString("N");
        Message = message;
        Type = type;
        Timestamp = DateTimeOffset.Now;
    }

    public string Id { get; }
    public string Message { get; }
    public NotificationType Type { get; }
    public DateTimeOffset Timestamp { get; }

    public string Icon => Type switch
    {
        NotificationType.Success => "✔",
        NotificationType.Warning => "⚠",
        NotificationType.Error => "✖",
        _ => "ℹ"
    };

    public string TimestampLabel => Timestamp.ToString("t");
}
