namespace gas_station.Models;

public class Station : BaseModel
{
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int BusinessId { get; set; }
    public int UserId { get; set; }
    public ICollection<FuelPrice> FuelPrices { get; set; } = new List<FuelPrice>();
}
