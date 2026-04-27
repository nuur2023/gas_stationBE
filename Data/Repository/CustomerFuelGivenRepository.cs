using backend.Common;
using backend.Data.Context;
using backend.Data.Interfaces;
using backend.Models;
using Microsoft.EntityFrameworkCore;

namespace backend.Data.Repository;

public class CustomerFuelGivenRepository(GasStationDBContext context) : ICustomerFuelGivenRepository
{
    private readonly GasStationDBContext _context = context;
    private DbSet<CustomerFuelGiven> Set => _context.Set<CustomerFuelGiven>();

    public async Task<CustomerFuelGiven> AddAsync(CustomerFuelGiven entity)
    {
        entity.CreatedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        await Set.AddAsync(entity);
        await _context.SaveChangesAsync();
        return entity;
    }

    public async Task<CustomerFuelGiven> UpdateAsync(int id, CustomerFuelGiven entity)
    {
        var existing = await Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"Entity with id {id} was not found.");

        _context.Entry(existing).CurrentValues.SetValues(entity);
        existing.Id = id;
        existing.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return existing;
    }

    public async Task<CustomerFuelGiven> DeleteAsync(int id)
    {
        var entity = await Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"Entity with id {id} was not found.");

        entity.IsDeleted = true;
        entity.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return entity;
    }

    public Task<List<CustomerFuelGiven>> GetAllAsync() => Set.Where(x => !x.IsDeleted).ToListAsync();

    public Task<CustomerFuelGiven?> GetByIdAsync(int id) =>
        Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);

    public async Task<PagedResult<CustomerFuelGiven>> GetPagedAsync(int page, int pageSize, string? search, int? businessId, int? stationId = null)
    {
        var q = Set.AsQueryable().Where(x => !x.IsDeleted);
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
                    EF.Functions.Like(x.Name, $"%{s}%") ||
                    EF.Functions.Like(x.Phone, $"%{s}%"));
            }
            else
            {
                q = q.Where(x =>
                    EF.Functions.Like(x.Name, $"%{s}%") ||
                    EF.Functions.Like(x.Phone, $"%{s}%") ||
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

        return new PagedResult<CustomerFuelGiven>(items, total, page, pageSize);
    }
}

