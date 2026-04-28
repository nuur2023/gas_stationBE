using gas_station.Common;
using gas_station.Data.Context;
using gas_station.Data.Interfaces;
using gas_station.Models;
using Microsoft.EntityFrameworkCore;

namespace gas_station.Data.Repository;

public class StationRepository(GasStationDBContext context) : IStationRepository
{
    private readonly GasStationDBContext _context = context;
    private DbSet<Station> Set => _context.Set<Station>();

    public async Task<Station> AddAsync(Station entity)
    {
        entity.CreatedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        await Set.AddAsync(entity);
        await _context.SaveChangesAsync();
        return entity;
    }

    public async Task<Station> UpdateAsync(int id, Station entity)
    {
        var existing = await Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"Entity with id {id} was not found.");

        _context.Entry(existing).CurrentValues.SetValues(entity);
        existing.Id = id;
        existing.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return existing;
    }

    public async Task<Station> DeleteAsync(int id)
    {
        var entity = await Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"Entity with id {id} was not found.");

        entity.IsDeleted = true;
        entity.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return entity;
    }

    public Task<Station?> GetByIdAsync(int id) => Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);

    public async Task<PagedResult<Station>> GetPagedAsync(int page, int pageSize, string? search, int? businessId)
    {
        var q = Set.AsQueryable().Where(x => !x.IsDeleted);
        if (businessId.HasValue)
        {
            q = q.Where(x => x.BusinessId == businessId.Value);
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            if (int.TryParse(s, out var n))
            {
                q = q.Where(x => x.Id == n || x.UserId == n);
            }
            else
            {
                q = q.Where(x => EF.Functions.Like(x.Name, $"%{s}%") || EF.Functions.Like(x.Address, $"%{s}%"));
            }
        }

        return await PagedQueryHelper.ToPagedAsync(q, page, pageSize);
    }
}
