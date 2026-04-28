using gas_station.Common;
using gas_station.Data.Context;
using gas_station.Data.Interfaces;
using gas_station.Models;
using gas_station.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace gas_station.Data.Repository;

public class InventoryRepository(GasStationDBContext context) : IInventoryRepository
{
    private readonly GasStationDBContext _context = context;

    /// <summary>All live inventory rows as list DTOs (denormalized from item + sale).</summary>
    private IQueryable<InventoryResponseDto> Rows() =>
        from item in _context.InventoryItems.AsNoTracking()
        join sale in _context.InventorySales.AsNoTracking() on item.InventorySaleId equals sale.Id
        where !item.IsDeleted && !sale.IsDeleted
        select new InventoryResponseDto
        {
            Id = item.Id,
            InventorySaleId = sale.Id,
            ReferenceNumber = sale.ReferenceNumber,
            EvidenceFilePath = sale.EvidenceFilePath,
            NozzleId = item.NozzleId,
            OpeningLiters = item.OpeningLiters,
            ClosingLiters = item.ClosingLiters,
            UsageLiters = item.UsageLiters,
            SspLiters = item.SspLiters,
            UsdLiters = item.UsdLiters,
            SspAmount = item.SspAmount,
            UsdAmount = item.UsdAmount,
            SspFuelPrice = item.SspFuelPrice,
            UsdFuelPrice = item.UsdFuelPrice,
            ExchangeRate = item.ExchangeRate,
            UserId = item.UserId,
            Date = item.Date,
            BusinessId = sale.BusinessId,
            StationId = sale.StationId,
        };

    public Task<List<InventoryResponseDto>> GetAllAsync() =>
        Rows().OrderByDescending(x => x.Date).ThenByDescending(x => x.Id).ToListAsync();

    public Task<InventoryResponseDto?> GetByIdAsync(int id) =>
        Rows().Where(x => x.Id == id).FirstOrDefaultAsync();

    public async Task<PagedResult<InventoryResponseDto>> GetPagedAsync(int page, int pageSize, string? search, int? businessId, int? stationId)
    {
        var q = Rows();

        if (businessId.HasValue)
        {
            q = q.Where(x => x.BusinessId == businessId.Value);
        }

        if (stationId.HasValue)
        {
            q = q.Where(x => x.StationId == stationId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            if (int.TryParse(s, out var n))
            {
                q = q.Where(x =>
                    x.Id == n ||
                    x.NozzleId == n ||
                    x.BusinessId == n ||
                    x.StationId == n ||
                    x.UserId == n ||
                    x.InventorySaleId == n);
            }
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 500);
        var total = await q.CountAsync();
        var slice = await q
            .OrderByDescending(x => x.Date)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        foreach (var row in slice)
        {
            if (row.EvidenceFilePath == "")
            {
                row.EvidenceFilePath = null;
            }
        }

        return new PagedResult<InventoryResponseDto>(slice, total, page, pageSize);
    }

    public async Task<InventoryResponseDto?> GetLatestByNozzleIdAsync(int nozzleId)
    {
        var row = await Rows()
            .Where(x => x.NozzleId == nozzleId)
            .OrderByDescending(x => x.Date)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync();
        if (row?.EvidenceFilePath == "")
        {
            row.EvidenceFilePath = null;
        }

        return row;
    }

    public async Task<InventoryItem> UpdateItemAsync(int id, InventoryItem entity)
    {
        var existing = await _context.InventoryItems.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"Inventory item {id} was not found.");

        var createdAt = existing.CreatedAt;
        entity.InventorySaleId = existing.InventorySaleId;
        entity.Id = id;
        _context.Entry(existing).CurrentValues.SetValues(entity);
        existing.CreatedAt = createdAt;
        existing.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return existing;
    }

    public async Task<InventoryItem> DeleteItemAsync(int id)
    {
        var entity = await _context.InventoryItems.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"Inventory item {id} was not found.");

        entity.IsDeleted = true;
        entity.UpdatedAt = DateTime.UtcNow;

        var saleId = entity.InventorySaleId;
        var remaining = await _context.InventoryItems.CountAsync(x => !x.IsDeleted && x.InventorySaleId == saleId && x.Id != id);
        if (remaining == 0)
        {
            var sale = await _context.InventorySales.FirstOrDefaultAsync(x => x.Id == saleId && !x.IsDeleted);
            if (sale is not null)
            {
                sale.IsDeleted = true;
                sale.UpdatedAt = DateTime.UtcNow;
            }
        }

        await _context.SaveChangesAsync();
        return entity;
    }
}
