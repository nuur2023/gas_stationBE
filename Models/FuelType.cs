namespace backend.Models;

public class FuelType : BaseModel
{
    public string FuelName { get; set; } = string.Empty;
    public int BusinessId { get; set; }
}
