using gas_station.Common;
using gas_station.Data.Context;
using gas_station.Data.Interfaces;
using gas_station.Models;
using Microsoft.EntityFrameworkCore;

namespace gas_station.Data.Repository;

public class AccountRepository(GasStationDBContext context) : IAccountRepository
{
    private readonly GasStationDBContext _context = context;
    private DbSet<Account> Set => _context.Set<Account>();

    public async Task<Account> AddAsync(Account entity)
    {
        entity.CreatedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        await Set.AddAsync(entity);
        await _context.SaveChangesAsync();
        return entity;
    }

    public async Task<Account> UpdateAsync(int id, Account entity)
    {
        var existing = await Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"Entity with id {id} was not found.");

        _context.Entry(existing).CurrentValues.SetValues(entity);
        existing.Id = id;
        existing.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return existing;
    }

    public async Task<Account> DeleteAsync(int id)
    {
        var entity = await Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"Entity with id {id} was not found.");

        entity.IsDeleted = true;
        entity.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return entity;
    }

    public Task<List<Account>> GetAllAsync() => Set
        .Where(x => !x.IsDeleted)
        .Include(x => x.ChartsOfAccounts)
        .ToListAsync();

    public Task<Account?> GetByIdAsync(int id) => Set
        .Include(x => x.ChartsOfAccounts)
        .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);

    public Task<Account?> GetByBusinessAndCodeAsync(int? businessId, string code) =>
        Set
            .Include(x => x.ChartsOfAccounts)
            .FirstOrDefaultAsync(x =>
                !x.IsDeleted &&
                x.Code == code &&
                (businessId == null ? x.BusinessId == null : x.BusinessId == businessId));

    public async Task<PagedResult<Account>> GetPagedAsync(int page, int pageSize, string? search, int? businessId)
    {
        IQueryable<Account> q = Set.AsQueryable()
            .Where(x => !x.IsDeleted)
            .Include(x => x.ChartsOfAccounts);

        if (businessId.HasValue)
            q = q.Where(x => x.BusinessId == businessId.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(x =>
                EF.Functions.Like(x.Name, $"%{s}%") ||
                EF.Functions.Like(x.Code, $"%{s}%") ||
                EF.Functions.Like(x.ChartsOfAccounts.Type, $"%{s}%"));
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 500);
        var total = await q.CountAsync();
        var items = await q
            .OrderBy(x => x.Code)
            .ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<Account>(items, total, page, pageSize);
    }

    public Task<List<Account>> GetParentCandidatesAsync(int businessId) =>
        Set
            .AsNoTracking()
            .Include(x => x.ChartsOfAccounts)
            .Where(x =>
                !x.IsDeleted &&
                x.ParentAccountId == null &&
                (x.BusinessId == null || x.BusinessId == businessId))
            .OrderBy(x => x.Code)
            .ThenBy(x => x.Id)
            .ToListAsync();
}

