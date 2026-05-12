using System.Globalization;
using gas_station.Common;
using gas_station.Data.Context;
using gas_station.Data.Interfaces;
using gas_station.Models;
using Microsoft.EntityFrameworkCore;

namespace gas_station.Data.Repository;

public class SupplierPaymentRepository(GasStationDBContext context) : ISupplierPaymentRepository
{
    private readonly GasStationDBContext _context = context;

    public async Task<PagedResult<SupplierPayment>> GetPagedAsync(int page, int pageSize, string? q, int? businessId)
    {
        var query =
            from p in _context.SupplierPayments.AsNoTracking()
            join s in _context.Suppliers.AsNoTracking() on p.SupplierId equals s.Id
            where !p.IsDeleted && !s.IsDeleted
            select new { p, s };

        if (businessId.HasValue)
            query = query.Where(x => x.p.BusinessId == businessId.Value);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(x =>
                (x.p.ReferenceNo != null && EF.Functions.Like(x.p.ReferenceNo, $"%{term}%")) ||
                EF.Functions.Like(x.s.Name, $"%{term}%"));
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 500);
        var ordered = query.OrderByDescending(x => x.p.Date).ThenByDescending(x => x.p.Id);
        var total = await ordered.CountAsync();
        var slice = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => x.p)
            .ToListAsync();

        return new PagedResult<SupplierPayment>(slice, total, page, pageSize);
    }

    public async Task<SupplierPayment> AddAsync(SupplierPayment entity)
    {
        var now = DateTime.UtcNow;
        entity.CreatedAt = now;
        entity.UpdatedAt = now;
        entity.IsDeleted = false;
        _context.SupplierPayments.Add(entity);
        await _context.SaveChangesAsync();
        return entity;
    }

    public async Task<SupplierPayment?> GetByIdAsync(int id) =>
        await _context.SupplierPayments.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);

    public async Task<bool> TryDeleteManualPaymentAsync(int id)
    {
        var e = await _context.SupplierPayments
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (e is null) return false;
        if (!string.Equals(e.Description, "Payment", StringComparison.OrdinalIgnoreCase) ||
            e.PurchaseId != null)
            return false;

        e.IsDeleted = true;
        e.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        await RecalculateSupplierBalancesAsync(e.BusinessId, e.SupplierId);
        return true;
    }

    public async Task<bool> TryUpdateManualPaymentAsync(int id, double paidAmount, DateTime dateUtc)
    {
        var e = await _context.SupplierPayments
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (e is null) return false;
        if (!string.Equals(e.Description, "Payment", StringComparison.OrdinalIgnoreCase) ||
            e.PurchaseId != null)
            return false;

        e.PaidAmount = Math.Round(paidAmount, 2, MidpointRounding.AwayFromZero);
        e.ChargedAmount = 0;
        e.Date = dateUtc;
        e.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        await RecalculateSupplierBalancesAsync(e.BusinessId, e.SupplierId);
        return true;
    }

    public async Task<double> GetSupplierBalanceAsync(int businessId, int supplierId)
    {
        if (businessId <= 0 || supplierId <= 0) return 0;
        var rows = _context.SupplierPayments.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BusinessId == businessId && x.SupplierId == supplierId);
        return await rows.SumAsync(x => x.ChargedAmount - x.PaidAmount);
    }

    public async Task<string> GenerateReferenceAsync(int businessId, DateTime date)
    {
        var dayKey = date.Date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var prefix = $"SP-{businessId}-{dayKey}-";
        var existingForDay = await _context.SupplierPayments.AsNoTracking()
            .Where(x => x.BusinessId == businessId
                        && x.ReferenceNo != null
                        && EF.Functions.Like(x.ReferenceNo, prefix + "%"))
            .Select(x => x.ReferenceNo!)
            .ToListAsync();

        var maxSeq = 0;
        foreach (var r in existingForDay)
        {
            var tail = r[prefix.Length..];
            if (int.TryParse(tail, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > maxSeq)
                maxSeq = n;
        }

        return $"{prefix}{(maxSeq + 1):0000}";
    }

    public async Task SyncPurchaseChargedTotalAndRecalculateBalancesAsync(int purchaseId)
    {
        var purchase = await _context.Purchases.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == purchaseId && !x.IsDeleted);
        if (purchase is null) return;

        var total = await _context.PurchaseItems.AsNoTracking()
            .Where(x => x.PurchaseId == purchaseId && !x.IsDeleted)
            .SumAsync(x => x.TotalAmount);
        var charged = Math.Round(total, 2, MidpointRounding.AwayFromZero);

        var ledger = await _context.SupplierPayments
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.PurchaseId == purchaseId && x.Description == "Purchased");
        if (ledger is null) return;

        ledger.ChargedAmount = charged;
        ledger.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        await RecalculateSupplierBalancesAsync(purchase.BusinessId, purchase.SupplierId);
    }

    public async Task RecalculateSupplierBalancesAsync(int businessId, int supplierId)
    {
        if (businessId <= 0 || supplierId <= 0) return;

        var rows = await _context.SupplierPayments
            .Where(x => !x.IsDeleted && x.BusinessId == businessId && x.SupplierId == supplierId)
            .OrderBy(x => x.Date)
            .ThenBy(x => x.Id)
            .ToListAsync();

        double running = 0;
        foreach (var r in rows)
        {
            running += r.ChargedAmount - r.PaidAmount;
            r.Balance = Math.Round(running, 2, MidpointRounding.AwayFromZero);
            r.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();
    }
}
