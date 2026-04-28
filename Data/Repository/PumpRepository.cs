using gas_station.Common;

using gas_station.Data.Context;

using gas_station.Data.Interfaces;

using gas_station.Models;

using Microsoft.EntityFrameworkCore;



namespace gas_station.Data.Repository;



public class PumpRepository(GasStationDBContext context) : IPumpRepository

{

    private readonly GasStationDBContext _context = context;

    private DbSet<Pump> Set => _context.Set<Pump>();



    public async Task<Pump> AddAsync(Pump entity)

    {

        entity.CreatedAt = DateTime.UtcNow;

        entity.UpdatedAt = DateTime.UtcNow;

        await Set.AddAsync(entity);

        await _context.SaveChangesAsync();

        return entity;

    }



    public async Task<Pump> UpdateAsync(int id, Pump entity)

    {

        var existing = await Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted)

            ?? throw new KeyNotFoundException($"Entity with id {id} was not found.");



        _context.Entry(existing).CurrentValues.SetValues(entity);

        existing.Id = id;

        existing.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return existing;

    }



    public async Task<Pump> DeleteAsync(int id)

    {

        var entity = await Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted)

            ?? throw new KeyNotFoundException($"Entity with id {id} was not found.");

        var nozzles = await _context.Set<Nozzle>().Where(x => !x.IsDeleted && x.PumpId == id).ToListAsync();
        foreach (var n in nozzles)
        {
            n.IsDeleted = true;
            n.UpdatedAt = DateTime.UtcNow;
        }

        var nozzleIds = nozzles.Select(n => n.Id).ToList();
        if (nozzleIds.Count > 0)
        {
            var links = await _context.Set<DippingPump>().Where(x => !x.IsDeleted && nozzleIds.Contains(x.NozzleId)).ToListAsync();
            foreach (var l in links)
            {
                l.IsDeleted = true;
                l.UpdatedAt = DateTime.UtcNow;
            }
        }

        entity.IsDeleted = true;

        entity.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return entity;

    }



    public Task<List<Pump>> GetAllAsync() => Set.Where(x => !x.IsDeleted).ToListAsync();



    public async Task<List<Pump>> GetFilteredAsync(int? dippingId, int? stationId, int? businessId)

    {

        var q = Set.AsQueryable().Where(x => !x.IsDeleted);

        if (stationId.HasValue)

        {

            q = q.Where(x => x.StationId == stationId.Value);

        }



        if (businessId.HasValue)

        {

            q = q.Where(x => x.BusinessId == businessId.Value);

        }

        if (dippingId.HasValue)
        {
            var nozzleIds = await _context.Set<DippingPump>().AsNoTracking()
                .Where(dp => !dp.IsDeleted && dp.DippingId == dippingId.Value)
                .Select(dp => dp.NozzleId)
                .ToListAsync();
            var pumpIds = await _context.Set<Nozzle>().AsNoTracking()
                .Where(n => !n.IsDeleted && nozzleIds.Contains(n.Id))
                .Select(n => n.PumpId)
                .Distinct()
                .ToListAsync();
            q = q.Where(p => pumpIds.Contains(p.Id));
        }



        return await q.OrderBy(x => x.Id).ToListAsync();

    }



    public async Task<PagedResult<Pump>> GetPagedAsync(int page, int pageSize, string? search, int? businessId, int? stationId = null)

    {

        var q = Set.AsQueryable().Where(x => !x.IsDeleted);

        if (businessId.HasValue)

        {

            q = q.Where(x => x.BusinessId == businessId.Value);

        }

        if (stationId is > 0)

        {

            q = q.Where(x => x.StationId == stationId.Value);

        }



        if (!string.IsNullOrWhiteSpace(search))

        {

            var s = search.Trim();

            if (int.TryParse(s, out var n))

            {

                q = q.Where(x => x.Id == n || x.StationId == n || x.BusinessId == n || EF.Functions.Like(x.PumpNumber, $"%{s}%"));

            }

            else

            {

                q = q.Where(x => EF.Functions.Like(x.PumpNumber, $"%{s}%"));

            }

        }



        page = Math.Max(1, page);

        pageSize = Math.Clamp(pageSize, 1, 500);

        var total = await q.CountAsync();

        var items = await q

            .OrderBy(x => x.Id)

            .Skip((page - 1) * pageSize)

            .Take(pageSize)

            .ToListAsync();



        return new PagedResult<Pump>(items, total, page, pageSize);

    }



    public Task<Pump?> GetByIdAsync(int id) => Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);

}

