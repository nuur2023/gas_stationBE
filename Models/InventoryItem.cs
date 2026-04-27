namespace backend.Models;

/// <summary>One nozzle line under an <see cref="InventorySale"/>.</summary>
public class InventoryItem : BaseModel
{
    public int InventorySaleId { get; set; }
    public int NozzleId { get; set; }
    public double OpeningLiters { get; set; }
    public double ClosingLiters { get; set; }
    public double UsageLiters { get; set; }
    public double SspLiters { get; set; }
    public double UsdLiters { get; set; }
    public double SspAmount { get; set; }
    public double UsdAmount { get; set; }
    public double SspFuelPrice { get; set; }
    public double UsdFuelPrice { get; set; }
    public double ExchangeRate { get; set; }
    public int UserId { get; set; }

    /// <summary>Same as parent sale <see cref="InventorySale.RecordedDate"/> for filtering/reporting.</summary>
    public DateTime Date { get; set; }
}
