namespace backend.Models;

public class PurchaseItem : BaseModel
{
    public int PurchaseId { get; set; }
    public int FuelTypeId { get; set; }
    public double Liters { get; set; }
    public double PricePerLiter { get; set; }
    public double TotalAmount { get; set; }
}
