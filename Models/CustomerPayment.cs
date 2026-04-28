using System.ComponentModel.DataAnnotations.Schema;

namespace gas_station.Models;

public class CustomerPayment : BaseModel
{
    public int CustomerFuelGivenId { get; set; }
    public double AmountPaid { get; set; }
    public DateTime PaymentDate { get; set; } = DateTime.UtcNow;

    public int BusinessId { get; set; }
    public int UserId { get; set; }

    /// <summary>Populated on list reads only; not persisted.</summary>
    [NotMapped]
    public string? UserName { get; set; }

    /// <summary>Amount still owed on the linked fuel given after all payments; list reads only.</summary>
    [NotMapped]
    public double? RemainingBalance { get; set; }

    /// <summary>Paid, Half-paid, Unpaid, or —; list reads only.</summary>
    [NotMapped]
    public string? PaymentStatus { get; set; }
}

