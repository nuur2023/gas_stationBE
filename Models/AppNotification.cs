namespace gas_station.Models;

/// <summary>In-app notification (e.g. pool transfer marked received at a station).</summary>
public class AppNotification : BaseModel
{
    public int BusinessId { get; set; }

    /// <summary>Destination station the transfer applies to (filtering in UI).</summary>
    public int StationId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public int? TransferInventoryId { get; set; }

    public int ConfirmedByUserId { get; set; }
}
