namespace backend.Models;

public class Dipping : BaseModel
{
    public string Name { get; set; } = string.Empty;

    public int FuelTypeId { get; set; }
    public double AmountLiter { get; set; }
    public int StationId { get; set; }
    public int BusinessId { get; set; }
    public int UserId { get; set; }
}
