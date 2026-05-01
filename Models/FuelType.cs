namespace gas_station.Models;

public class FuelType : BaseModel
{
    public string FuelName { get; set; } = string.Empty;
    public int BusinessId { get; set; }
    public ICollection<FuelPrice> FuelPrices { get; set; } = new List<FuelPrice>();
    public ICollection<GeneratorUsage> GeneratorUsages { get; set; } = new List<GeneratorUsage>();
}
