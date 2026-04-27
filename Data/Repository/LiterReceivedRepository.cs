using backend.Common;
using backend.Data.Context;
using backend.Data.Interfaces;
using backend.Models;
using Microsoft.EntityFrameworkCore;

namespace backend.Data.Repository;

public class LiterReceivedRepository(GasStationDBContext context) : ILiterReceivedRepository
{
    private readonly GasStationDBContext _context = context;
    private DbSet<LiterReceived> Set => _context.Set<LiterReceived>();

    public async Task<LiterReceived> AddAsync(LiterReceived entity)
    {
        entity.CreatedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        await Set.AddAsync(entity);
        await _context.SaveChangesAsync();
        return entity;
    }

    public async Task<LiterReceived> UpdateAsync(int id, LiterReceived entity)
    {
        var existing = await Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"Entity with id {id} was not found.");

        _context.Entry(existing).CurrentValues.SetValues(entity);
        existing.Id = id;
        existing.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return existing;
    }

    public async Task<LiterReceived> DeleteAsync(int id)
    {
        var entity = await Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"Entity with id {id} was not found.");

        entity.IsDeleted = true;
        entity.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return entity;
    }

    public Task<LiterReceived?> GetByIdAsync(int id) => Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);

    public async Task<PagedResult<LiterReceived>> GetPagedAsync(
        int page,
        int pageSize,
        string? search,
        int? businessId,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        int? stationId = null)
    {
        var q = Set.AsQueryable().Where(x => !x.IsDeleted);
        if (businessId.HasValue)
        {
            q = q.Where(x => x.BusinessId == businessId.Value);
        }

        if (stationId is > 0)
        {
            var sid = stationId.Value;
            q = q.Where(x => x.StationId == sid || x.ToStationId == sid);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            if (int.TryParse(s, out var n))
            {
                q = q.Where(x =>
                    x.Id == n ||
                    x.FuelTypeId == n ||
                    x.StationId == n ||
                    x.UserId == n ||
                    x.ToStationId == n ||
                    x.FromStationId == n);
            }
            else
            {
                q = q.Where(x =>
                    EF.Functions.Like(x.Name, $"%{s}%") ||
                    EF.Functions.Like(x.Type, $"%{s}%") ||
                    EF.Functions.Like(x.Targo, $"%{s}%") ||
                    EF.Functions.Like(x.DriverName, $"%{s}%"));
            }
        }

        if (fromDate.HasValue)
        {
            var startUtc = DateTime.SpecifyKind(fromDate.Value.Date, DateTimeKind.Utc);
            q = q.Where(x => x.CreatedAt >= startUtc);
        }

        if (toDate.HasValue)
        {
            var endExclusive = DateTime.SpecifyKind(toDate.Value.Date, DateTimeKind.Utc).AddDays(1);
            q = q.Where(x => x.CreatedAt < endExclusive);
        }

        return await PagedQueryHelper.ToPagedAsync(q, page, pageSize);
    }
}
