using gas_station.Common;
using gas_station.Data.Context;
using gas_station.Data.Interfaces;
using gas_station.Models;
using Microsoft.EntityFrameworkCore;

namespace gas_station.Data.Repository;

public class MenuRepository(GasStationDBContext context) : IMenuRepository
{
    private readonly GasStationDBContext _context = context;
    private DbSet<Menu> Set => _context.Set<Menu>();

    public async Task<Menu> AddAsync(Menu entity)
    {
        entity.CreatedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        await Set.AddAsync(entity);
        await _context.SaveChangesAsync();
        return entity;
    }

    public async Task<Menu> UpdateAsync(int id, Menu entity)
    {
        var existing = await Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"Entity with id {id} was not found.");

        _context.Entry(existing).CurrentValues.SetValues(entity);
        existing.Id = id;
        existing.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return existing;
    }

    public async Task<Menu> DeleteAsync(int id)
    {
        var entity = await Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"Entity with id {id} was not found.");

        entity.IsDeleted = true;
        entity.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return entity;
    }

    public Task<List<Menu>> GetAllAsync() => Set.Where(x => !x.IsDeleted).ToListAsync();

    public async Task<List<Menu>> GetTreeAsync()
    {
        return await Set
            .Where(m => !m.IsDeleted)
            .Include(m => m.SubMenus.Where(s => !s.IsDeleted))
            .OrderBy(m => m.Id)
            .ToListAsync();
    }

    public Task<Menu?> GetByIdAsync(int id) => Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);

    public async Task<PagedResult<Menu>> GetPagedAsync(int page, int pageSize, string? search)
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
