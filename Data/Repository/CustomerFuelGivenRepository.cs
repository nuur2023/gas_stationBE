using gas_station.Common;
using gas_station.Data.Context;
using gas_station.Data.Interfaces;
using gas_station.Models;
using Microsoft.EntityFrameworkCore;

namespace gas_station.Data.Repository;

public class CustomerFuelGivenRepository(GasStationDBContext context) : ICustomerFuelGivenRepository
{
    private readonly GasStationDBContext _context = context;
    private DbSet<CustomerFuelTransaction> Set => _context.Set<CustomerFuelTransaction>();

    public async Task<CustomerFuelTransaction> AddAsync(CustomerFuelTransaction entity)
    {
        entity.CreatedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        await Set.AddAsync(entity);
        await _context.SaveChangesAsync();
        return entity;
    }

    public async Task<CustomerFuelTransaction> UpdateAsync(int id, CustomerFuelTransaction entity)
    {
        var existing = await Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"Entity with id {id} was not found.");

        _context.Entry(existing).CurrentValues.SetValues(entity);
        existing.Id = id;
        existing.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return existing;
    }

    public async Task<CustomerFuelTransaction> DeleteAsync(int id)
    {
        var entity = await Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"Entity with id {id} was not found.");

        entity.IsDeleted = true;
        entity.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return entity;
    }

    public Task<List<CustomerFuelTransaction>> GetAllAsync() => Set.Where(x => !x.IsDeleted).ToListAsync();

    public Task<CustomerFuelTransaction?> GetByIdAsync(int id) =>
        Set.Include(x => x.Customer).FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);

    public async Task<PagedResult<CustomerFuelTransaction>> GetPagedAsync(int page, int pageSize, string? search, int? businessId, int? stationId = null)
    {
        var q = Set.Include(x => x.Customer).AsQueryable().Where(x => !x.IsDeleted);
        if (businessId.HasValue)
        {
            q = q.Where(x => x.BusinessId == businessId.Value);
        }

        if (stationId is > 0)
        {
            q = q.Where(x => x.StationId == stationId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            if (int.TryParse(s, out var n))
            {
                q = q.Where(x =>
                    x.Id == n ||
                    x.FuelTypeId == n ||
                    x.StationId == n ||
                    EF.Functions.Like(x.Customer.Name, $"%{s}%") ||
                    EF.Functions.Like(x.Customer.Phone, $"%{s}%"));
            }
            else
            {
                q = q.Where(x =>
                    EF.Functions.Like(x.Customer.Name, $"%{s}%") ||
                    EF.Functions.Like(x.Customer.Phone, $"%{s}%") ||
                    EF.Functions.Like(x.Remark ?? "", $"%{s}%"));
            }
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

        return new PagedResult<CustomerFuelTransaction>(items, total, page, pageSize);
    }
}

