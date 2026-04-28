using gas_station.Data.Context;
using gas_station.Data.Interfaces;
using gas_station.Models;
using Microsoft.EntityFrameworkCore;

namespace gas_station.Data.Repository;

public class DippingPumpRepository(GasStationDBContext context) : IDippingPumpRepository
{
    private readonly GasStationDBContext _context = context;
    private DbSet<DippingPump> Set => _context.Set<DippingPump>();

    public async Task<int?> GetDippingIdByNozzleIdAsync(int nozzleId)
    {
        var dipId = await Set.AsNoTracking()
            .Where(x => !x.IsDeleted && x.NozzleId == nozzleId)
            .OrderBy(x => x.Id)
            .Select(x => x.DippingId)
            .FirstOrDefaultAsync();
        return dipId <= 0 ? null : dipId;
    }

    public Task<DippingPump?> GetFirstByNozzleIdAsync(int nozzleId) =>
        Set.Where(x => !x.IsDeleted && x.NozzleId == nozzleId).OrderBy(x => x.Id).FirstOrDefaultAsync();

    public async Task<DippingPump> AddAsync(DippingPump entity)
    {
        entity.CreatedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        await Set.AddAsync(entity);
        await _context.SaveChangesAsync();
        return entity;
    }

    public async Task<DippingPump> UpdateAsync(int id, DippingPump entity)
    {
        var existing = await Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"DippingPump {id} was not found.");
        _context.Entry(existing).CurrentValues.SetValues(entity);
        existing.Id = id;
        existing.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return existing;
    }

    public async Task SoftDeleteByNozzleIdAsync(int nozzleId)
    {
        var list = await Set.Where(x => !x.IsDeleted && x.NozzleId == nozzleId).ToListAsync();
        foreach (var d in list)
        {
            d.IsDeleted = true;
            d.UpdatedAt = DateTime.UtcNow;
        }

        if (list.Count > 0)
            await _context.SaveChangesAsync();
    }
}
