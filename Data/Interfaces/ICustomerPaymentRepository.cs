using gas_station.Common;
using gas_station.Models;

namespace gas_station.Data.Interfaces;

public interface ICustomerPaymentRepository : IGasStationInterface<CustomerPayment>
{
    Task<List<CustomerPayment>> GetAllAsync();
    Task<CustomerPayment?> GetByIdAsync(int id);
    Task<PagedResult<CustomerPayment>> GetPagedAsync(int page, int pageSize, string? search, int? businessId, int? filterStationId = null);

    /// <summary>
    /// Current outstanding balance for a customer within a business:
    /// sum(ChargedAmount - AmountPaid) across non-deleted ledger rows.
    /// </summary>
    Task<double> GetCustomerBalanceAsync(int businessId, int customerId);

    /// <summary>
    /// Generates a unique reference number scoped to the business
    /// (CP-{BusinessId}-{YYYYMMDD}-{seq}).
    /// </summary>
    Task<string> GenerateReferenceAsync(int businessId, DateTime date);

    /// <summary>
    /// Syncs the customer "Charged" ledger total from non-deleted customer transactions,
    /// then recomputes running Balance snapshots for the customer.
    /// </summary>
    /// <param name="actingUserId">JWT user id; required when a new Charged ledger row is inserted (FK to Users).</param>
    Task SyncCustomerChargedTotalAndRecalculateBalancesAsync(int businessId, int customerId, int actingUserId);

    /// <summary>
    /// Recomputes Balance on every non-deleted ledger row for the customer
    /// in chronological order.
    /// </summary>
    Task RecalculateCustomerBalancesAsync(int businessId, int customerId);
}
