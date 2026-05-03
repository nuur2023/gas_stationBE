namespace gas_station.Models;

/// <summary>Transfer of liters from business pool to a station (distribution record).</summary>
public class TransferInventory : BaseModel
{
    public int BusinessFuelInventoryId { get; set; }
    public int ToStationId { get; set; }
    public double Liters { get; set; }
    public DateTime Date { get; set; }
    public int CreatorId { get; set; }
    public string? Note { get; set; }

    public TransferInventoryStatus Status { get; set; } = TransferInventoryStatus.Pending;
}
