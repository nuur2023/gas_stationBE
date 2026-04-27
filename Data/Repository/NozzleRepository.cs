using backend.Data.Context;
using backend.Data.Interfaces;
using backend.Models;
using Microsoft.EntityFrameworkCore;

namespace backend.Data.Repository;

public class NozzleRepository(GasStationDBContext context) : INozzleRepository
{
    private readonly GasStationDBContext _context = context;
    private DbSet<Nozzle> Set => _context.Set<Nozzle>();

    public Task<Nozzle?> GetByIdAsync(int id) =>
        Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);

    public Task<List<Nozzle>> ListByStationAsync(int stationId, int businessId) =>
        Set.AsNoTracking()
            .Where(x => !x.IsDeleted && x.StationId == stationId && x.BusinessId == businessId)
            .OrderBy(x => x.PumpId)
            .ThenBy(x => x.Id)
            .ToListAsync();

    public Task<List<Nozzle>> ListByBusinessAsync(int businessId) =>
        Set.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BusinessId == businessId)
            .OrderBy(x => x.StationId)
            .ThenBy(x => x.PumpId)
            .ThenBy(x => x.Id)
            .ToListAsync();

    public Task<List<Nozzle>> ListByPumpIdAsync(int pumpId) =>
        Set.Where(x => !x.IsDeleted && x.PumpId == pumpId).OrderBy(x => x.Id).ToListAsync();

    public async Task<Nozzle> AddAsync(Nozzle entity)
    {
        entity.CreatedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        await Set.AddAsync(entity);
        await _context.SaveChangesAsync();
        return entity;
    }

    public async Task<Nozzle> UpdateAsync(int id, Nozzle entity)
    {
        var existing = await Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"Nozzle {id} was not found.");
        _context.Entry(existing).CurrentValues.SetValues(entity);
        existing.Id = id;
        existing.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return existing;
    }

    public async Task<Nozzle> DeleteAsync(int id)
    {
        var entity = await Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"Nozzle {id} was not found.");
        entity.IsDeleted = true;
        entity.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return entity;
    }

    public async Task SoftDeleteByPumpIdAsync(int pumpId)
    {
        var list = await Set.Where(x => !x.IsDeleted && x.PumpId == pumpId).ToListAsync();
        foreach (var n in list)
        {
            n.IsDeleted = true;
            n.UpdatedAt = DateTime.UtcNow;
        }

        if (list.Count > 0)
            await _context.SaveChangesAsync();
    }
}
