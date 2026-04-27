using backend.Common;
using backend.Data.Context;
using backend.Data.Interfaces;
using backend.Models;
using Microsoft.EntityFrameworkCore;

namespace backend.Data.Repository;

public class BusinessRepository(GasStationDBContext context) : IBusinessRepository
{
    private readonly GasStationDBContext _context = context;
    private DbSet<Business> Set => _context.Set<Business>();

    public async Task<Business> AddAsync(Business entity)
    {
        entity.CreatedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        await Set.AddAsync(entity);
        await _context.SaveChangesAsync();
        return entity;
    }

    public async Task<Business> UpdateAsync(int id, Business entity)
    {
        var existing = await Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"Entity with id {id} was not found.");

        _context.Entry(existing).CurrentValues.SetValues(entity);
        existing.Id = id;
        existing.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return existing;
    }

    public async Task<Business> DeleteAsync(int id)
    {
        var entity = await Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"Entity with id {id} was not found.");

        entity.IsDeleted = true;
        entity.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return entity;
    }

    public Task<List<Business>> GetAllAsync() => Set.Where(x => !x.IsDeleted).ToListAsync();

    public Task<Business?> GetByIdAsync(int id) => Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);

    public async Task<PagedResult<Business>> GetPagedAsync(int page, int pageSize, string? search)
    {
        var q = Set.AsQueryable().Where(x => !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(x =>
                EF.Functions.Like(x.Name, $"%{s}%")
                || (x.Address != null && EF.Functions.Like(x.Address, $"%{s}%"))
                || (x.PhoneNumber != null && EF.Functions.Like(x.PhoneNumber, $"%{s}%")));
        }

        return await PagedQueryHelper.ToPagedAsync(q, page, pageSize);
    }
}
