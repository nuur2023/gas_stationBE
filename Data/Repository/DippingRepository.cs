using backend.Common;
using backend.Data.Context;
using backend.Data.Interfaces;
using backend.Models;
using Microsoft.EntityFrameworkCore;

namespace backend.Data.Repository;

public class DippingRepository(GasStationDBContext context) : IDippingRepository
{
    private readonly GasStationDBContext _context = context;
    private DbSet<Dipping> Set => _context.Set<Dipping>();

    public async Task<Dipping> AddAsync(Dipping entity)
    {
        entity.CreatedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        await Set.AddAsync(entity);
        await _context.SaveChangesAsync();
        return entity;
    }

    public async Task<Dipping> UpdateAsync(int id, Dipping entity)
    {
        var existing = await Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"Entity with id {id} was not found.");

        _context.Entry(existing).CurrentValues.SetValues(entity);
        existing.Id = id;
        existing.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return existing;
    }

    public async Task<Dipping> DeleteAsync(int id)
    {
        var entity = await Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"Entity with id {id} was not found.");

        entity.IsDeleted = true;
        entity.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return entity;
    }

    public Task<Dipping?> GetByIdAsync(int id) => Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);

    public Task<Dipping?> GetFirstByStationAndFuelAsync(int stationId, int fuelTypeId) =>
        Set.Where(x => !x.IsDeleted && x.StationId == stationId && x.FuelTypeId == fuelTypeId)
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync();

    public async Task<PagedResult<Dipping>> GetPagedAsync(int page, int pageSize, string? search, int? businessId, int? stationId)
    {
        var q = Set.AsQueryable().Where(x => !x.IsDeleted);
        if (businessId.HasValue)
        {
            q = q.Where(x => x.BusinessId == businessId.Value);
        }
        if (stationId.HasValue)
        {
            q = q.Where(x => x.StationId == stationId.Value);
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            if (int.TryParse(s, out var n))
            {
                q = q.Where(x => x.Id == n || x.FuelTypeId == n || x.StationId == n || x.UserId == n);
            }
        }

        return await PagedQueryHelper.ToPagedAsync(q, page, pageSize);
    }
}
