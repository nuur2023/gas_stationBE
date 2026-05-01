namespace gas_station.Models;

/// <summary>Immutable audit trail for create/update/delete on <see cref="TransferInventory"/>.</summary>
public class TransferInventoryAudit : BaseModel
{
    public int TransferInventoryId { get; set; }

    /// <summary>Created, Updated, or Deleted.</summary>
    public string Action { get; set; } = string.Empty;

    public DateTime ChangedAt { get; set; }
    public int ChangedByUserId { get; set; }
    public string? Reason { get; set; }

    /// <summary>JSON snapshot of relevant fields before the change (null on Created).</summary>
    public string? BeforeJson { get; set; }

    /// <summary>JSON snapshot after the change (null on Deleted).</summary>
    public string? AfterJson { get; set; }
}
