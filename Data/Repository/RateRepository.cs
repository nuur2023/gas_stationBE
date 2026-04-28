using gas_station.Common;
using gas_station.Data.Context;
using gas_station.Data.Interfaces;
using gas_station.Models;
using gas_station.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace gas_station.Data.Repository;

public class RateRepository(GasStationDBContext context) : IRateRepository
{
    private readonly GasStationDBContext _context = context;
    private DbSet<Rate> Set => _context.Set<Rate>();

    public async Task<Rate> AddAsync(Rate entity)
    {
        entity.CreatedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        await Set.AddAsync(entity);
        await _context.SaveChangesAsync();
        return entity;
    }

    public async Task<Rate> UpdateAsync(int id, Rate entity)
    {
        var existing = await Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"Entity with id {id} was not found.");

        _context.Entry(existing).CurrentValues.SetValues(entity);
        existing.Id = id;
        existing.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return existing;
    }

    public async Task<Rate> DeleteAsync(int id)
    {
        var entity = await Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"Entity with id {id} was not found.");

        entity.IsDeleted = true;
        entity.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return entity;
    }

    public Task<List<Rate>> GetAllAsync() => Set.Where(x => !x.IsDeleted).ToListAsync();

    public Task<Rate?> GetByIdAsync(int id) => Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);

    public async Task<PagedResult<RateViewModel>> GetPagedAsync(int page, int pageSize, string? search, int? businessId)
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
                q = q.Where(x => x.Id == n || x.UsersId == n || x.BusinessId == n || x.RateNumber == n);
            }
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 500);
        var total = await q.CountAsync();

        var users = _context.Users.AsNoTracking().Where(u => !u.IsDeleted);
        var pageQuery = q
            .OrderByDescending(r => r.Date)
            .ThenByDescending(r => r.Id)
            .Select(r => new RateViewModel
            {
                Id = r.Id,
                RateNumber = r.RateNumber,
                BusinessId = r.BusinessId,
                UsersId = r.UsersId,
                UserName = users.Where(u => u.Id == r.UsersId).Select(u => u.Name).FirstOrDefault(),
                Date = r.Date,
                Active = r.Active,
            });

        var items = await pageQuery
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<RateViewModel>(items, total, page, pageSize);
    }
}
