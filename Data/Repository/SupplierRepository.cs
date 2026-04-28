using gas_station.Common;
using gas_station.Data.Context;
using gas_station.Data.Interfaces;
using gas_station.Models;
using Microsoft.EntityFrameworkCore;

namespace gas_station.Data.Repository;

public class SupplierRepository(GasStationDBContext context) : ISupplierRepository
{
    private readonly GasStationDBContext _context = context;
    private DbSet<Supplier> Set => _context.Set<Supplier>();

    public async Task<Supplier> AddAsync(Supplier entity)
    {
        entity.CreatedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        await Set.AddAsync(entity);
        await _context.SaveChangesAsync();
        return entity;
    }

    public async Task<Supplier> UpdateAsync(int id, Supplier entity)
    {
        var existing = await Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"Entity with id {id} was not found.");

        _context.Entry(existing).CurrentValues.SetValues(entity);
        existing.Id = id;
        existing.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return existing;
    }

    public async Task<Supplier> DeleteAsync(int id)
    {
        var entity = await Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"Entity with id {id} was not found.");

        entity.IsDeleted = true;
        entity.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return entity;
    }

    public Task<Supplier?> GetByIdAsync(int id) => Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);

    public async Task<PagedResult<Supplier>> GetPagedAsync(int page, int pageSize, string? search, int? businessId)
    {
        var q = Set.AsQueryable().Where(x => !x.IsDeleted);
        if (businessId.HasValue)
        {
            q = q.Where(x => x.BusinessId == businessId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(x =>
                EF.Functions.Like(x.Name, $"%{s}%") ||
                EF.Functions.Like(x.Phone, $"%{s}%") ||
                EF.Functions.Like(x.Email, $"%{s}%") ||
                EF.Functions.Like(x.Address, $"%{s}%"));
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 500);
        var total = await q.CountAsync();
        var items = await q
            .OrderBy(x => x.Name)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<Supplier>(items, total, page, pageSize);
    }
}
