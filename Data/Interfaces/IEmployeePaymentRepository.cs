using gas_station.Common;
using gas_station.Models;

namespace gas_station.Data.Interfaces;

public interface IEmployeePaymentRepository : IGasStationInterface<EmployeePayment>
{
    Task<EmployeePayment?> GetByIdAsync(int id);

    Task<PagedResult<EmployeePayment>> GetPagedAsync(
        int page,
        int pageSize,
        string? search,
        int? businessId,
        int? stationId,
        int? employeeId,
        string? period);

    /// <summary>
    /// Outstanding balance for an employee within a business: sum(ChargedAmount - PaidAmount).
    /// </summary>
    Task<double> GetEmployeeBalanceAsync(int businessId, int employeeId);

    /// <summary>Generates a unique reference scoped to the business: EP-{BusinessId}-{YYYYMMDD}-{seq}.</summary>
    Task<string> GenerateReferenceAsync(int businessId, DateTime date);

    /// <summary>Recomputes Balance on every non-deleted ledger row for the employee in chronological order.</summary>
    Task RecalculateEmployeeBalancesAsync(int businessId, int employeeId);

    /// <summary>
    /// Creates one "Salary" charge + one "Payment" row per non-excluded item in the run.
    /// Returns the list of inserted ledger rows. The caller is responsible for resolving
    /// the actor (UserId) and target business / station.
    /// </summary>
    Task<List<EmployeePayment>> CreatePayrollRunAsync(
        int businessId,
        int? stationId,
        int userId,
        string period,
        DateTime paymentDate,
        IReadOnlyList<(int EmployeeId, double Charged, double Paid)> items);
}
