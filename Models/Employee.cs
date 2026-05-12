namespace gas_station.Models;

/// <summary>
/// Master record for an employee that draws a salary. Optional <see cref="StationId"/>
/// lets admins associate an employee with a specific station for filtering, but it can
/// also be left null (business-level employee, mirrors the Management Expense pattern).
/// </summary>
public class Employee : BaseModel
{
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    /// <summary>Free-text job title / position (e.g. "Cashier", "Pump attendant").</summary>
    public string Position { get; set; } = string.Empty;
    /// <summary>Default amount accrued per payroll cycle. May be 0 for hourly / commissioned staff.</summary>
    public double BaseSalary { get; set; }
    /// <summary>Inactive employees are hidden from payroll runs and the unpaid report by default.</summary>
    public bool IsActive { get; set; } = true;
    public int BusinessId { get; set; }
    /// <summary>Optional station scoping — null for business-level employees.</summary>
    public int? StationId { get; set; }
}
