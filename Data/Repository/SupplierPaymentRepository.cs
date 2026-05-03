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
}
