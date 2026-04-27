using backend.Common;
using backend.Data.Context;
using backend.Data.Interfaces;
using backend.Models;
using Microsoft.EntityFrameworkCore;

namespace backend.Data.Repository;

public class PermissionRepository(GasStationDBContext context) : IPermissionRepository
{
    private readonly GasStationDBContext _context = context;
    private DbSet<Permission> Set => _context.Set<Permission>();

    public async Task<Permission> AddAsync(Permission entity)
    {
        entity.CreatedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        await Set.AddAsync(entity);
        await _context.SaveChangesAsync();
        return entity;
    }

    public async Task<Permission> UpdateAsync(int id, Permission entity)
    {
        var existing = await Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"Entity with id {id} was not found.");

        _context.Entry(existing).CurrentValues.SetValues(entity);
        existing.Id = id;
        existing.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return existing;
    }

    public async Task<Permission> DeleteAsync(int id)
    {
        var entity = await Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"Entity with id {id} was not found.");

        entity.IsDeleted = true;
        entity.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return entity;
    }

    public Task<List<Permission>> GetAllAsync() => Set.Where(x => !x.IsDeleted).ToListAsync();

    public Task<List<Permission>> GetByUserAndBusinessAsync(int userId, int businessId) =>
        Set.Where(x => !x.IsDeleted && x.UserId == userId && x.BusinessId == businessId).ToListAsync();

    public async Task ReplaceForUserAndBusinessAsync(int userId, int businessId, IReadOnlyList<Permission> newPermissions)
    {
        await using var tx = await _context.Database.BeginTransactionAsync();
        var old = await Set.Where(x => x.UserId == userId && x.BusinessId == businessId).ToListAsync();
        static string Key(int menuId, int? subMenuId) => $"{menuId}:{subMenuId?.ToString() ?? "null"}";
        var oldByKey = old.ToDictionary(x => Key(x.MenuId, x.SubMenuId), x => x);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var now = DateTime.UtcNow;
        foreach (var p in newPermissions)
        {
            var key = Key(p.MenuId, p.SubMenuId);
            seen.Add(key);
            if (oldByKey.TryGetValue(key, out var existing))
            {
                existing.CanView = p.CanView;
                existing.CanCreate = p.CanCreate;
                existing.CanUpdate = p.CanUpdate;
                existing.CanDelete = p.CanDelete;
                existing.IsDeleted = false;
                existing.UpdatedAt = now;
            }
            else
            {
                p.Id = 0;
                p.UserId = userId;
                p.BusinessId = businessId;
                p.CreatedAt = now;
                p.UpdatedAt = now;
                p.IsDeleted = false;
                await Set.AddAsync(p);
            }
        }

        foreach (var existing in old)
        {
            var key = Key(existing.MenuId, existing.SubMenuId);
            if (seen.Contains(key)) continue;
            existing.IsDeleted = true;
            existing.UpdatedAt = now;
        }

        await _context.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public Task<Permission?> GetByIdAsync(int id) => Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);

    public async Task<PagedResult<Permission>> GetPagedAsync(int page, int pageSize, string? search)
    {
        var q = Set.AsQueryable().Where(x => !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            if (int.TryParse(s, out var n))
            {
                q = q.Where(x => x.UserId == n || x.BusinessId == n || x.MenuId == n || x.SubMenuId == n);
            }
        }

        return await PagedQueryHelper.ToPagedAsync(q, page, pageSize);
    }
}
