using gas_station.Common;
using gas_station.Models;

namespace gas_station.Data.Interfaces;

public interface ISupplierPaymentRepository
{
    Task<PagedResult<SupplierPayment>> GetPagedAsync(int page, int pageSize, string? q, int? businessId);
    Task<SupplierPayment?> GetByIdAsync(int id);
    Task<SupplierPayment> AddAsync(SupplierPayment entity);

    /// <summary>
    /// Current outstanding balance for a supplier within a business: sum(ChargedAmount - PaidAmount)
    /// across non-deleted ledger rows.
    /// </summary>
    Task<double> GetSupplierBalanceAsync(int businessId, int supplierId);

    /// <summary>
    /// Generates a unique reference number scoped to the business (SP-{BusinessId}-{YYYYMMDD}-{seq}).
    /// </summary>
    Task<string> GenerateReferenceAsync(int businessId, DateTime date);

    /// <summary>
    /// Updates the "Purchased" ledger row for this purchase to match line-item totals, then recomputes
    /// running Balance snapshots for the supplier.
    /// </summary>
    Task SyncPurchaseChargedTotalAndRecalculateBalancesAsync(int purchaseId);

    /// <summary>
    /// Recomputes Balance on every non-deleted ledger row for the supplier in chronological order.
    /// </summary>
    Task RecalculateSupplierBalancesAsync(int businessId, int supplierId);

    /// <summary>Soft-deletes a manual payment row (Description = Payment, no PurchaseId). Returns false if not allowed or missing.</summary>
    Task<bool> TryDeleteManualPaymentAsync(int id);

    /// <summary>Updates paid amount and date on a manual payment row. Returns false if not allowed or missing.</summary>
    Task<bool> TryUpdateManualPaymentAsync(int id, double paidAmount, DateTime dateUtc);
}
