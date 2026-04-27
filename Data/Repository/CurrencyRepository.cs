using backend.Data.Context;
using backend.Data.Interfaces;
using backend.Models;
using Microsoft.EntityFrameworkCore;

namespace backend.Data.Repository;

public class CurrencyRepository(GasStationDBContext context) : ICurrencyRepository
{
    private readonly GasStationDBContext _context = context;
    private DbSet<Currency> Set => _context.Set<Currency>();

    public async Task<Currency> AddAsync(Currency entity)
    {
        entity.CreatedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        await Set.AddAsync(entity);
        await _context.SaveChangesAsync();
        return entity;
    }

    public async Task<Currency> UpdateAsync(int id, Currency entity)
    {
        var existing = await Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"Entity with id {id} was not found.");

        // Do not SetValues(entity): that copies Id/CreatedAt/IsDeleted and EF cannot mark key properties modified.
        existing.CountryName = entity.CountryName;
        existing.Code = entity.Code;
        existing.Name = entity.Name;
        existing.Symbol = entity.Symbol;
        existing.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return existing;
    }

    public async Task<Currency> DeleteAsync(int id)
    {
        var entity = await Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"Entity with id {id} was not found.");

        entity.IsDeleted = true;
        entity.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return entity;
    }

    public Task<List<Currency>> GetAllAsync() => Set.Where(x => !x.IsDeleted).ToListAsync();

    public Task<Currency?> GetByIdAsync(int id) => Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
}
