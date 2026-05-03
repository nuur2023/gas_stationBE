namespace gas_station.Models;

/// <summary>Per-user read receipt for <see cref="AppNotification"/>.</summary>
public class AppNotificationRead
{
    public int Id { get; set; }

    public int AppNotificationId { get; set; }
    public AppNotification AppNotification { get; set; } = null!;

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public DateTime ReadAtUtc { get; set; }
}
