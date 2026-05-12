namespace gas_station.Models;

/// <summary>
/// Records every supplier ledger movement: a charge from a saved purchase ("Purchased")
/// or a manual payment to the supplier ("Payment"). The running supplier balance is
/// snapshotted on every row so the Supplier Report can render history without recomputation.
/// </summary>
public class SupplierPayment : BaseModel
{
    public string? ReferenceNo { get; set; }
    public int SupplierId { get; set; }
    /// <summary>"Purchased" when created from a Purchase, "Payment" when entered manually.</summary>
    public string Description { get; set; } = "Payment";
    /// <summary>Amount the supplier charged us (purchase total). 0 for payments.</summary>
    public double ChargedAmount { get; set; }
    /// <summary>Amount paid to the supplier on this row. 0 for purchase rows.</summary>
    public double PaidAmount { get; set; }
    /// <summary>Snapshot of the supplier's outstanding balance after this row was applied.</summary>
    public double Balance { get; set; }
    /// <summary>Set when this row was auto-generated from a Purchase; null for manual payments.</summary>
    public int? PurchaseId { get; set; }
    public DateTime Date { get; set; }
    public int BusinessId { get; set; }
    public int UserId { get; set; }
}
