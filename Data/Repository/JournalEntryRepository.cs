using gas_station.Common;
using gas_station.Data.Context;
using gas_station.Data.Interfaces;
using gas_station.Models;
using Microsoft.EntityFrameworkCore;

namespace gas_station.Data.Repository;

public class JournalEntryRepository(GasStationDBContext context) : IJournalEntryRepository
{
    private readonly GasStationDBContext _context = context;
    private DbSet<JournalEntry> Set => _context.Set<JournalEntry>();

    public async Task<JournalEntry> AddAsync(JournalEntry entity)
    {
        entity.CreatedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        await Set.AddAsync(entity);
        await _context.SaveChangesAsync();
        return entity;
    }

    public async Task<JournalEntry> UpdateAsync(int id, JournalEntry entity)
    {
        var existing = await Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"Entity with id {id} was not found.");

        _context.Entry(existing).CurrentValues.SetValues(entity);
        existing.Id = id;
        existing.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return existing;
    }

    public async Task<JournalEntry> DeleteAsync(int id)
    {
        var entity = await Set
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"Entity with id {id} was not found.");

        var now = DateTime.UtcNow;
        foreach (var line in entity.Lines.Where(l => !l.IsDeleted))
        {
            line.IsDeleted = true;
            line.UpdatedAt = now;
        }

        entity.IsDeleted = true;
        entity.UpdatedAt = now;
        await _context.SaveChangesAsync();
        return entity;
    }

    public Task<List<JournalEntry>> GetAllAsync() => Set
        .Where(x => !x.IsDeleted)
        .Include(x => x.Lines)
        .ToListAsync();

    public Task<JournalEntry?> GetByIdAsync(int id) => Set
        .Include(x => x.Lines)
        .ThenInclude(l => l.Account)
        .ThenInclude(a => a.ChartsOfAccounts)
        .Include(x => x.Lines)
        .ThenInclude(l => l.Customer)
        .Include(x => x.Lines)
        .ThenInclude(l => l.Supplier)
        .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);

    public async Task<PagedResult<JournalEntry>> GetPagedAsync(int page, int pageSize, string? search, int? businessId, int? filterStationId)
    {
        var q = Set.AsQueryable().Where(x => !x.IsDeleted);
        if (businessId.HasValue) q = q.Where(x => x.BusinessId == businessId.Value);
        if (filterStationId.HasValue) q = q.Where(x => x.StationId == filterStationId.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(x => EF.Functions.Like(x.Description, $"%{s}%"));
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 500);
        var total = await q.CountAsync();
        var items = await q
            .Include(x => x.Lines)
            .OrderByDescending(x => x.Date)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<JournalEntry>(items, total, page, pageSize);
    }

    public async Task<JournalEntry?> UpdateHeaderAsync(int id, string description, DateTime? dateUtc = null)
    {
        var row = await Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (row is null) return null;
        row.Description = description;
        if (dateUtc.HasValue) row.Date = dateUtc.Value;
        row.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return await GetByIdAsync(id);
    }
}

