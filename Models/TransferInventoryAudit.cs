namespace gas_station.Models;

/// <summary>Immutable audit trail for create/update/delete on <see cref="TransferInventory"/>.</summary>
public class TransferInventoryAudit : BaseModel
{
    public int TransferInventoryId { get; set; }

    /// <summary>Created, Updated, or Deleted.</summary>
    public string Action { get; set; } = string.Empty;

    public DateTime ChangedAt { get; set; }
    public int ChangedByUserId { get; set; }

    /// <summary>Station and liters snapshot for this audit row.</summary>
    public int ToStationId { get; set; }

    public double Liters { get; set; }
    public DateTime Date { get; set; }

    public string? Reason { get; set; }

    public int BusinessId { get; set; }
}
