using System.ComponentModel.DataAnnotations.Schema;

namespace gas_station.Models;

/// <summary>
/// Employee ledger row — mirrors the supplier / customer ledger pattern.
/// "Salary" rows are charges accrued by a payroll run. "Payment" rows are
/// disbursements (regular salary, advance, bonus). <see cref="Balance"/> is a
/// snapshot of the employee's outstanding balance (sum charges − sum payments)
/// after this row was applied.
/// </summary>
public class EmployeePayment : BaseModel
{
    public int EmployeeId { get; set; }
    public string? ReferenceNo { get; set; }
    /// <summary>"Salary" for accrual rows; "Payment" / "Advance" / "Bonus" for disbursements.</summary>
    public string Description { get; set; } = "Payment";
    /// <summary>Amount accrued (e.g. monthly salary). 0 for payment rows.</summary>
    public double ChargedAmount { get; set; }
    /// <summary>Amount paid out on this row. 0 for accrual rows.</summary>
    public double PaidAmount { get; set; }
    /// <summary>Snapshot of the employee's outstanding balance after this row was applied.</summary>
    public double Balance { get; set; }
    public DateTime PaymentDate { get; set; } = DateTime.UtcNow;
    /// <summary>Period the row belongs to (e.g. "2026-05") — set by payroll runs, optional otherwise.</summary>
    public string? PeriodLabel { get; set; }

    public int BusinessId { get; set; }
    public int UserId { get; set; }
    /// <summary>Optional station context — payroll runs may target a station; ad-hoc payments may leave it null.</summary>
    public int? StationId { get; set; }

    /// <summary>Populated on list reads only; not persisted.</summary>
    [NotMapped]
    public string? UserName { get; set; }

    /// <summary>Employee name copied for display on list reads only; not persisted.</summary>
    [NotMapped]
    public string? EmployeeName { get; set; }

    /// <summary>Outstanding balance for the employee after every row; list reads only.</summary>
    [NotMapped]
    public double? RemainingBalance { get; set; }
}
