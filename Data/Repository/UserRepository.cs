using gas_station.Common;
using gas_station.Data.Context;
using gas_station.Data.Interfaces;
using gas_station.Models;
using Microsoft.EntityFrameworkCore;

namespace gas_station.Data.Repository;

public class UserRepository(GasStationDBContext context) : IUserRepository
{
    private readonly GasStationDBContext _context = context;
    private DbSet<User> Set => _context.Set<User>();

    public async Task<User> AddAsync(User entity)
    {
        entity.CreatedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        await Set.AddAsync(entity);
        await _context.SaveChangesAsync();
        return entity;
    }

    public async Task<User> UpdateAsync(int id, User entity)
    {
        var existing = await Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"Entity with id {id} was not found.");

        _context.Entry(existing).CurrentValues.SetValues(entity);
        existing.Id = id;
        existing.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return existing;
    }

    public async Task<User> DeleteAsync(int id)
    {
        var entity = await Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"Entity with id {id} was not found.");

        var links = await _context.BusinessUsers
            .Where(x => !x.IsDeleted && x.UserId == id)
            .ToListAsync();
        foreach (var link in links)
        {
            link.IsDeleted = true;
            link.UpdatedAt = DateTime.UtcNow;
        }

        entity.IsDeleted = true;
        entity.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return entity;
    }

    public Task<List<User>> GetAllAsync() => Set.Where(x => !x.IsDeleted).ToListAsync();

    public Task<User?> GetByIdAsync(int id) => Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);

    public async Task<PagedResult<User>> GetPagedAsync(
        int page,
        int pageSize,
        string? search,
        int? businessId = null,
        bool includeElevatedRoles = true)
    {
        var q = Set.AsQueryable().Where(x => !x.IsDeleted);
        if (businessId is > 0)
        {
            q = q.Where(u =>
                _context.BusinessUsers.Any(bu => !bu.IsDeleted && bu.BusinessId == businessId.Value && bu.UserId == u.Id)
                || !_context.BusinessUsers.Any(bu => !bu.IsDeleted && bu.UserId == u.Id));
        }
        if (!includeElevatedRoles)
        {
            q = q.Where(u =>
                !_context.Roles.Any(r =>
                    r.Id == u.RoleId &&
                    !r.IsDeleted &&
                    (r.Name == "SuperAdmin" || r.Name == "Admin")));
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            var pattern = $"%{s}%";
            q = q.Where(x =>
                EF.Functions.Like(x.Name, pattern) ||
                EF.Functions.Like(x.Email, pattern) ||
                EF.Functions.Like(x.Phone, pattern));
        }

        return await PagedQueryHelper.ToPagedAsync(q, page, pageSize);
    }
}
