namespace gas_station.ViewModels;

public class EmployeeWriteRequestViewModel
{
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? Position { get; set; }
    /// <summary>Default monthly accrual; parsed with InvariantCulture. "0" if not on a fixed salary.</summary>
    public string BaseSalary { get; set; } = "0";
    public bool IsActive { get; set; } = true;
    public int BusinessId { get; set; }
    /// <summary>Optional — null for business-level employees.</summary>
    public int? StationId { get; set; }
}

public class EmployeePaymentWriteRequestViewModel
{
    public int EmployeeId { get; set; }
    /// <summary>"Payment" (default) / "Advance" / "Bonus" / "Salary".</summary>
    public string? Description { get; set; }
    /// <summary>Disbursement amount; parsed with InvariantCulture.</summary>
    public string AmountPaid { get; set; } = "0";
    /// <summary>Optional accrual recorded on the same row (rare; usually use payroll-run for accruals).</summary>
    public string? ChargedAmount { get; set; }
    public DateTimeOffset? PaymentDate { get; set; }
    /// <summary>Optional period this payment belongs to, e.g. "2026-05".</summary>
    public string? PeriodLabel { get; set; }
    public int BusinessId { get; set; }
    public int? StationId { get; set; }
}

public class PayrollRunItemViewModel
{
    public int EmployeeId { get; set; }
    /// <summary>Salary accrued for this employee this period; parsed with InvariantCulture.</summary>
    public string ChargedAmount { get; set; } = "0";
    /// <summary>Cash actually paid out to this employee (may differ from charged for partial pay).</summary>
    public string AmountPaid { get; set; } = "0";
    /// <summary>If true, this employee is skipped in this run (no rows created).</summary>
    public bool Excluded { get; set; }
}

public class PayrollRunWriteRequestViewModel
{
    /// <summary>Period label, e.g. "2026-05".</summary>
    public string Period { get; set; } = string.Empty;
    public DateTimeOffset? PaymentDate { get; set; }
    public int BusinessId { get; set; }
    public int? StationId { get; set; }
    public List<PayrollRunItemViewModel> Items { get; set; } = new();
}
