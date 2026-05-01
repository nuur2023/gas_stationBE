namespace gas_station.Models;

/// <summary>Business-level fuel pool balance per fuel type (separate from dipping / nozzle inventory).</summary>
public class BusinessFuelInventory : BaseModel
{
    public int BusinessId { get; set; }
    public int FuelTypeId { get; set; }
    public double Liters { get; set; }
}
