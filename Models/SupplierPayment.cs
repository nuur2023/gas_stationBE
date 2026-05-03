namespace gas_station.Models;

/// <summary>Records a payment to a supplier (e.g. linked to a purchase or entered manually).</summary>
public class SupplierPayment : BaseModel
{
    public string? ReferenceNo { get; set; }
    public int SupplierId { get; set; }
    public double Amount { get; set; }
    public DateTime Date { get; set; }
    public int BusinessId { get; set; }
    public int UserId { get; set; }
}
