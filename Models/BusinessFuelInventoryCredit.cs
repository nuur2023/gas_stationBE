namespace gas_station.Models;

/// <summary>Audit of liters added into the business fuel pool (stock-in events).</summary>
public class BusinessFuelInventoryCredit : BaseModel
{
    public int BusinessId { get; set; }
    public int FuelTypeId { get; set; }
    public double Liters { get; set; }
    public DateTime Date { get; set; }
    public int CreatorId { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string? Note { get; set; }
}
