using gas_station.Common;
using gas_station.Data.Context;
using gas_station.Data.Interfaces;
using gas_station.Models;
using Microsoft.EntityFrameworkCore;

namespace gas_station.Data.Repository;

public class GeneratorUsageRepository(GasStationDBContext context) : IGeneratorUsageRepository
{
    private readonly GasStationDBContext _context = context;
    private DbSet<GeneratorUsage> Set => _context.Set<GeneratorUsage>();

    public async Task<GeneratorUsage> AddAsync(GeneratorUsage entity)
    {
        entity.CreatedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        await Set.AddAsync(entity);
        await _context.SaveChangesAsync();
        return entity;
    }

    public async Task<GeneratorUsage> UpdateAsync(int id, GeneratorUsage entity)
    {
        var existing = await Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"Entity with id {id} was not found.");

        _context.Entry(existing).CurrentValues.SetValues(entity);
        existing.Id = id;
        existing.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return existing;
    }

    public async Task<GeneratorUsage> DeleteAsync(int id)
    {
        var entity = await Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"Entity with id {id} was not found.");

        entity.IsDeleted = true;
        entity.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return entity;
    }

    public Task<List<GeneratorUsage>> GetAllAsync() => Set.Where(x => !x.IsDeleted).ToListAsync();

    public Task<GeneratorUsage?> GetByIdAsync(int id) => Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);

    public async Task<PagedResult<GeneratorUsage>> GetPagedAsync(int page, int pageSize, string? search, int? businessId, int? stationId = null)
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
                    x.Id == n
                    || x.UsersId == n
                    || x.BusinessId == n
                    || x.StationId == n
                    || x.FuelTypeId == n);
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

        return new PagedResult<GeneratorUsage>(items, total, page, pageSize);
    }
}
