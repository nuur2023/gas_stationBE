using backend.Common;
using backend.Data.Context;
using backend.Data.Interfaces;
using backend.Models;
using Microsoft.EntityFrameworkCore;

namespace backend.Data.Repository;

public class BusinessUserRepository(GasStationDBContext context) : IBusinessUserRepository
{
    private readonly GasStationDBContext _context = context;
    private DbSet<BusinessUser> Set => _context.Set<BusinessUser>();

    public async Task<BusinessUser> AddAsync(BusinessUser entity)
    {
        entity.CreatedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        await Set.AddAsync(entity);
        await _context.SaveChangesAsync();
        return entity;
    }

    public async Task<BusinessUser> UpdateAsync(int id, BusinessUser entity)
    {
        var existing = await Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"Entity with id {id} was not found.");

        existing.UserId = entity.UserId;
        existing.BusinessId = entity.BusinessId;
        existing.StationId = entity.StationId;
        existing.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return existing;
    }

    public async Task<BusinessUser> DeleteAsync(int id)
    {
        var entity = await Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"Entity with id {id} was not found.");

        entity.IsDeleted = true;
        entity.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return entity;
    }

    public Task<List<BusinessUser>> GetAllAsync() => Set.Where(x => !x.IsDeleted).ToListAsync();

    public Task<BusinessUser?> GetByIdAsync(int id) => Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);

    public async Task<PagedResult<BusinessUser>> GetPagedAsync(
        int page,
        int pageSize,
        string? search,
        int? businessId = null,
        bool includeElevatedRoles = true)
    {
        var q = Set.AsQueryable().Where(x => !x.IsDeleted);
        if (businessId is > 0)
        {
            q = q.Where(x => x.BusinessId == businessId.Value);
        }
        if (!includeElevatedRoles)
        {
            q = q.Where(x =>
                !_context.Users.Any(u =>
                    !u.IsDeleted &&
                    u.Id == x.UserId &&
                    _context.Roles.Any(r =>
                        !r.IsDeleted &&
                        r.Id == u.RoleId &&
                        (r.Name == "SuperAdmin" || r.Name == "Admin"))));
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            if (int.TryParse(s, out var n))
            {
                q = q.Where(x => x.UserId == n || x.BusinessId == n || x.StationId == n);
            }
        }

        return await PagedQueryHelper.ToPagedAsync(q, page, pageSize);
    }

    public Task<bool> LinkExistsAsync(int userId, int businessId, int stationId, int? excludeId = null)
    {
        var q = Set.AsNoTracking()
            .Where(x => !x.IsDeleted && x.UserId == userId && x.BusinessId == businessId && x.StationId == stationId);
        if (excludeId is > 0)
            q = q.Where(x => x.Id != excludeId.Value);
        return q.AnyAsync();
    }
}
