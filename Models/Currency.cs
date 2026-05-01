namespace gas_station.Models;

public class Currency : BaseModel
{
    public string CountryName { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public ICollection<FuelPrice> FuelPrices { get; set; } = new List<FuelPrice>();
}
