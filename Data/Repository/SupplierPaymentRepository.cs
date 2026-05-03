using gas_station.Common;
using gas_station.Data.Context;
using gas_station.Data.Interfaces;
using gas_station.Models;
using Microsoft.EntityFrameworkCore;

namespace gas_station.Data.Repository;

public class SupplierPaymentRepository(GasStationDBContext context) : ISupplierPaymentRepository
{
    private readonly GasStationDBContext _context = context;
    private DbSet<SupplierPayment> Set => _context.Set<SupplierPayment>();

    public async Task<PagedResult<SupplierPayment>> GetPagedAsync(int page, int pageSize, string? search, int? businessId)
    {
        var q = Set.AsQueryable().Where(x => !x.IsDeleted);
        if (businessId.HasValue)
            q = q.Where(x => x.BusinessId == businessId.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            if (int.TryParse(s, out var n))
                q = q.Where(x => x.Id == n || x.SupplierId == n || (x.ReferenceNo != null && EF.Functions.Like(x.ReferenceNo, $"%{s}%")));
            else
                q = q.Where(x => x.ReferenceNo != null && EF.Functions.Like(x.ReferenceNo, $"%{s}%"));
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 500);
        var total = await q.CountAsync();
        var items = await q
            .OrderByDescending(x => x.Date)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<SupplierPayment>(items, total, page, pageSize);
    }

    public async Task<SupplierPayment> AddAsync(SupplierPayment entity)
    {
        entity.CreatedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.IsDeleted = false;
        await Set.AddAsync(entity);
        await _context.SaveChangesAsync();
        return entity;
    }
}
