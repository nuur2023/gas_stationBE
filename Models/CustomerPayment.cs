using System.ComponentModel.DataAnnotations.Schema;

namespace gas_station.Models;

/// <summary>
/// Customer ledger row. Mirrors the supplier ledger pattern:
/// "Charged" rows are auto-created from customer transactions; "Payment" rows
/// are entered manually. <see cref="Balance"/> is a snapshot of the running balance
/// for the customer after this row.
/// </summary>
public class CustomerPayment : BaseModel
{
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public string? ReferenceNo { get; set; }
    /// <summary>"Charged" when auto-created from a CustomerFuelGiven, "Payment" when entered manually.</summary>
    public string Description { get; set; } = "Payment";
    /// <summary>Amount charged to the customer (fuel total or cash advance). 0 for payments.</summary>
    public double ChargedAmount { get; set; }
    /// <summary>Amount paid by the customer on this row. 0 for charged rows.</summary>
    public double AmountPaid { get; set; }
    /// <summary>Snapshot of the customer's outstanding balance after this row was applied.</summary>
    public double Balance { get; set; }
    public DateTime PaymentDate { get; set; } = DateTime.UtcNow;

    public int BusinessId { get; set; }
    public int UserId { get; set; }

    /// <summary>Populated on list reads only; not persisted.</summary>
    [NotMapped]
    public string? UserName { get; set; }

    /// <summary>Amount still owed by the customer after all rows; list reads only.</summary>
    [NotMapped]
    public double? RemainingBalance { get; set; }

    /// <summary>Paid, Half-paid, Unpaid, or —; list reads only.</summary>
    [NotMapped]
    public string? PaymentStatus { get; set; }
}
