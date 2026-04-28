using gas_station.Data.Context;
using gas_station.Data.Interfaces;
using gas_station.Models;
using Microsoft.EntityFrameworkCore;

namespace gas_station.Data.Repository;

public class FuelTypeRepository(GasStationDBContext context) : IFuelTypeRepository
{
    private readonly GasStationDBContext _context = context;
    private DbSet<FuelType> Set => _context.Set<FuelType>();

    public async Task<FuelType> AddAsync(FuelType entity)
    {
        entity.CreatedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        await Set.AddAsync(entity);
        await _context.SaveChangesAsync();
        return entity;
    }

    public async Task<FuelType> UpdateAsync(int id, FuelType entity)
    {
        var existing = await Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"Entity with id {id} was not found.");

        _context.Entry(existing).CurrentValues.SetValues(entity);
        existing.Id = id;
        existing.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return existing;
    }

    public async Task<FuelType> DeleteAsync(int id)
    {
        var entity = await Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"Entity with id {id} was not found.");

        entity.IsDeleted = true;
        entity.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return entity;
    }

    public Task<List<FuelType>> GetAllAsync() => Set.Where(x => !x.IsDeleted).ToListAsync();

    public Task<FuelType?> GetByIdAsync(int id) => Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
}
