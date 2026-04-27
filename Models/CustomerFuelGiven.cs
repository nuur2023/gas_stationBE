namespace backend.Models;

public class CustomerFuelGiven : BaseModel
{
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public int FuelTypeId { get; set; }
    public double GivenLiter { get; set; }
    public double Price { get; set; }
    public double UsdAmount { get; set; }
    public string? Remark { get; set; }
    public int StationId { get; set; }
    public int BusinessId { get; set; }
    public DateTime Date { get; set; } = DateTime.UtcNow;
}
