using gas_station.Common;
using gas_station.Data.Context;
using gas_station.Data.Interfaces;
using gas_station.Models;
using Microsoft.EntityFrameworkCore;

namespace gas_station.Data.Repository;

public class SubMenuRepository(GasStationDBContext context) : ISubMenuRepository
{
    private readonly GasStationDBContext _context = context;
    private DbSet<SubMenu> Set => _context.Set<SubMenu>();

    public async Task<SubMenu> AddAsync(SubMenu entity)
    {
        entity.CreatedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        await Set.AddAsync(entity);
        await _context.SaveChangesAsync();
        return entity;
    }

    public async Task<SubMenu> UpdateAsync(int id, SubMenu entity)
    {
        var existing = await Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"Entity with id {id} was not found.");

        _context.Entry(existing).CurrentValues.SetValues(entity);
        existing.Id = id;
        existing.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return existing;
    }

    public async Task<SubMenu> DeleteAsync(int id)
    {
        var entity = await Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"Entity with id {id} was not found.");

        entity.IsDeleted = true;
        entity.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return entity;
    }

    public Task<List<SubMenu>> GetAllAsync() => Set.Where(x => !x.IsDeleted).ToListAsync();

    public Task<SubMenu?> GetByIdAsync(int id) => Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);

    public async Task<PagedResult<SubMenu>> GetPagedAsync(int page, int pageSize, string? search)
    {
        var q = Set.AsQueryable().Where(x => !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(x => EF.Functions.Like(x.Name, $"%{s}%") || EF.Functions.Like(x.Route, $"%{s}%"));
        }

        return await PagedQueryHelper.ToPagedAsync(q, page, pageSize);
    }
}
