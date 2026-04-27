using backend.Data.Context;
using backend.Data.Interfaces;
using backend.Models;
using Microsoft.EntityFrameworkCore;

namespace backend.Data.Repository;

public class FuelPriceRepository(GasStationDBContext context) : IFuelPriceRepository
{
    private readonly GasStationDBContext _context = context;
    private DbSet<FuelPrice> Set => _context.Set<FuelPrice>();

    public async Task<FuelPrice> AddAsync(FuelPrice entity)
    {
        entity.CreatedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        await Set.AddAsync(entity);
        await _context.SaveChangesAsync();
        return entity;
    }

    public async Task<FuelPrice> UpdateAsync(int id, FuelPrice entity)
    {
        var existing = await Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"Entity with id {id} was not found.");

        existing.FuelTypeId = entity.FuelTypeId;
        existing.StationId = entity.StationId;
        existing.BusinessId = entity.BusinessId;
        existing.Price = entity.Price;
        existing.CurrencyId = entity.CurrencyId;
        existing.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return existing;
    }

    public async Task<FuelPrice> DeleteAsync(int id)
    {
        var entity = await Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"Entity with id {id} was not found.");

        entity.IsDeleted = true;
        entity.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return entity;
    }

    public Task<List<FuelPrice>> GetAllAsync() => Set
        .Where(x => !x.IsDeleted)
        .Include(x => x.FuelType)
        .Include(x => x.Station)
        .Include(x => x.Currency)
        .ToListAsync();

    public Task<FuelPrice?> GetByIdAsync(int id) => Set
        .Include(x => x.FuelType)
        .Include(x => x.Station)
        .Include(x => x.Currency)
        .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
}
