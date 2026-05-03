using gas_station.Common;
using gas_station.Data.Context;
using gas_station.Data.Interfaces;
using gas_station.Models;
using gas_station.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace gas_station.Data.Repository;

public class PurchaseRepository(GasStationDBContext context) : IPurchaseRepository
{
    private readonly GasStationDBContext _context = context;
    private DbSet<Purchase> Set => _context.Set<Purchase>();
    private DbSet<PurchaseItem> ItemSet => _context.Set<PurchaseItem>();

    public Task<Purchase?> GetByIdAsync(int id) => Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);

    public async Task<PurchaseDetailResponse?> GetDetailAsync(int id)
    {
        var p = await Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (p is null) return null;

        var items = await ItemSet
            .Where(x => x.PurchaseId == id)
            .OrderBy(x => x.Id)
            .ToListAsync();

        return ToDetail(p, items);
    }

    public async Task<PagedResult<Purchase>> GetPagedAsync(int page, int pageSize, string? search, int? businessId)
    {
        var q = Set.AsQueryable().Where(x => !x.IsDeleted);
        if (businessId.HasValue)
        {
            q = q.Where(x => x.BusinessId == businessId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            if (int.TryParse(s, out var n))
            {
                q = q.Where(x => x.Id == n || x.SupplierId == n || EF.Functions.Like(x.InvoiceNo, $"%{s}%"));
            }
            else
            {
                q = q.Where(x => EF.Functions.Like(x.InvoiceNo, $"%{s}%"));
            }
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 500);
        var total = await q.CountAsync();
        var items = await q
            .OrderByDescending(x => x.PurchaseDate)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<Purchase>(items, total, page, pageSize);
    }

    public async Task<PurchaseDetailResponse> AddWithItemsAsync(Purchase purchase, IReadOnlyList<PurchaseItem> items, SupplierPayment? supplierPayment = null)
    {
        await using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            purchase.CreatedAt = DateTime.UtcNow;
            purchase.UpdatedAt = DateTime.UtcNow;
            await Set.AddAsync(purchase);
            await _context.SaveChangesAsync();

            if (items.Count > 0)
            {
                foreach (var line in items)
                {
                    line.PurchaseId = purchase.Id;
                    line.CreatedAt = DateTime.UtcNow;
                    line.UpdatedAt = DateTime.UtcNow;
                    line.IsDeleted = false;
                }

                await ItemSet.AddRangeAsync(items);
                await _context.SaveChangesAsync();
            }

            if (supplierPayment is not null)
            {
                supplierPayment.CreatedAt = DateTime.UtcNow;
                supplierPayment.UpdatedAt = DateTime.UtcNow;
                supplierPayment.IsDeleted = false;
                await _context.Set<SupplierPayment>().AddAsync(supplierPayment);
                await _context.SaveChangesAsync();
            }

            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }

        return (await GetDetailAsync(purchase.Id))!;
    }

    public async Task<PurchaseDetailResponse?> UpdateHeaderAsync(int id, Purchase purchase)
    {
        var existing = await Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (existing is null) return null;

        existing.SupplierId = purchase.SupplierId;
        existing.InvoiceNo = purchase.InvoiceNo;
        existing.BusinessId = purchase.BusinessId;
        existing.PurchaseDate = purchase.PurchaseDate;
        existing.Status = purchase.Status;
        existing.AmountPaid = purchase.AmountPaid;
        existing.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return await GetDetailAsync(id);
    }

    public async Task<PurchaseDetailResponse?> AddItemAsync(int purchaseId, PurchaseItem item)
    {
        var p = await GetByIdAsync(purchaseId);
        if (p is null) return null;

        item.PurchaseId = purchaseId;
        item.CreatedAt = DateTime.UtcNow;
        item.UpdatedAt = DateTime.UtcNow;
        item.IsDeleted = false;
        await ItemSet.AddAsync(item);
        await _context.SaveChangesAsync();
        return await GetDetailAsync(purchaseId);
    }

    public async Task<PurchaseDetailResponse?> UpdateItemAsync(int purchaseId, int itemId, PurchaseItem patch)
    {
        var line = await ItemSet.FirstOrDefaultAsync(x => x.Id == itemId && x.PurchaseId == purchaseId);
        if (line is null) return null;

        line.FuelTypeId = patch.FuelTypeId;
        line.Liters = patch.Liters;
        line.PricePerLiter = patch.PricePerLiter;
        line.TotalAmount = patch.TotalAmount;
        line.IsDeleted = false;
        line.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return await GetDetailAsync(purchaseId);
    }

    public async Task<PurchaseDetailResponse?> DeleteItemAsync(int purchaseId, int itemId)
    {
        var line = await ItemSet.FirstOrDefaultAsync(x => x.Id == itemId && x.PurchaseId == purchaseId);
        if (line is null) return null;
        if (line.IsDeleted) return await GetDetailAsync(purchaseId);

        line.IsDeleted = true;
        line.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return await GetDetailAsync(purchaseId);
    }

    /// <summary>
    /// Soft-deletes the purchase and every line item (application-level cascade).
    /// The database FK uses ON DELETE CASCADE for physical deletes if you ever hard-delete a purchase row.
    /// </summary>
    public async Task DeleteAsync(int id)
    {
        var p = await Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"Purchase {id} was not found.");

        await using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            p.IsDeleted = true;
            p.UpdatedAt = DateTime.UtcNow;

            var lines = await ItemSet.Where(x => x.PurchaseId == id && !x.IsDeleted).ToListAsync();
            foreach (var line in lines)
            {
                line.IsDeleted = true;
                line.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    private static PurchaseDetailResponse ToDetail(Purchase p, List<PurchaseItem> items) =>
        new()
        {
            Id = p.Id,
            SupplierId = p.SupplierId,
            InvoiceNo = p.InvoiceNo,
            BusinessId = p.BusinessId,
            PurchaseDate = p.PurchaseDate,
            Status = p.Status,
            AmountPaid = p.AmountPaid,
            CreatedAt = p.CreatedAt,
            UpdatedAt = p.UpdatedAt,
            Items = items
                .Select(i => new PurchaseItemResponse
                {
                    Id = i.Id,
                    PurchaseId = i.PurchaseId,
                    FuelTypeId = i.FuelTypeId,
                    Liters = i.Liters,
                    PricePerLiter = i.PricePerLiter,
                    TotalAmount = i.TotalAmount,
                    IsDeleted = i.IsDeleted,
                })
                .ToList(),
        };
}
