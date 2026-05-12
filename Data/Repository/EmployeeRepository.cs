using gas_station.Common;
using gas_station.Data.Context;
using gas_station.Data.Interfaces;
using gas_station.Models;
using Microsoft.EntityFrameworkCore;

namespace gas_station.Data.Repository;

public class EmployeeRepository(GasStationDBContext context) : IEmployeeRepository
{
    private readonly GasStationDBContext _context = context;
    private DbSet<Employee> Set => _context.Set<Employee>();

    public async Task<Employee> AddAsync(Employee entity)
    {
        var now = DateTime.UtcNow;
        entity.CreatedAt = now;
        entity.UpdatedAt = now;
        entity.IsDeleted = false;
        await Set.AddAsync(entity);
        await _context.SaveChangesAsync();
        return entity;
    }

    public async Task<Employee> UpdateAsync(int id, Employee entity)
    {
        var existing = await Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"Entity with id {id} was not found.");

        existing.Name = entity.Name;
        existing.Phone = entity.Phone;
        existing.Email = entity.Email;
        existing.Address = entity.Address;
        existing.Position = entity.Position;
        existing.BaseSalary = entity.BaseSalary;
        existing.IsActive = entity.IsActive;
        existing.BusinessId = entity.BusinessId;
        existing.StationId = entity.StationId;
        existing.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return existing;
    }

    public async Task<Employee> DeleteAsync(int id)
    {
        var entity = await Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"Entity with id {id} was not found.");

        entity.IsDeleted = true;
        entity.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return entity;
    }

    public Task<Employee?> GetByIdAsync(int id) => Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);

    public async Task<PagedResult<Employee>> GetPagedAsync(
        int page,
        int pageSize,
        string? search,
        int? businessId,
        int? stationId,
        bool includeInactive)
    {
        var q = Set.AsQueryable().Where(x => !x.IsDeleted);
        if (businessId.HasValue) q = q.Where(x => x.BusinessId == businessId.Value);
        if (stationId.HasValue) q = q.Where(x => x.StationId == stationId.Value);
        if (!includeInactive) q = q.Where(x => x.IsActive);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(x =>
                EF.Functions.Like(x.Name, $"%{s}%") ||
                EF.Functions.Like(x.Phone, $"%{s}%") ||
                EF.Functions.Like(x.Position, $"%{s}%") ||
                EF.Functions.Like(x.Email, $"%{s}%"));
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 500);
        var total = await q.CountAsync();
        var items = await q
            .OrderBy(x => x.Name)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<Employee>(items, total, page, pageSize);
    }

    public Task<List<Employee>> GetActiveAsync(int businessId, int? stationId)
    {
        var q = Set.AsNoTracking().Where(x => !x.IsDeleted && x.BusinessId == businessId && x.IsActive);
        if (stationId.HasValue && stationId.Value > 0)
            q = q.Where(x => x.StationId == stationId.Value);
        return q.OrderBy(x => x.Name).ToListAsync();
    }
}
