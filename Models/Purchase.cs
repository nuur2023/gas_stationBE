namespace gas_station.Models;

public class Purchase : BaseModel
{
    public int SupplierId { get; set; }
    public string InvoiceNo { get; set; } = string.Empty;
    public int BusinessId { get; set; }
    public DateTime PurchaseDate { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = "Unpaid";
    public double AmountPaid { get; set; }
}
