using backend.Common;
using backend.Data.Context;
using backend.Data.Interfaces;
using backend.Models;
using Microsoft.EntityFrameworkCore;

namespace backend.Data.Repository;

public class CustomerPaymentRepository(GasStationDBContext context) : ICustomerPaymentRepository
{
    private readonly GasStationDBContext _context = context;
    private DbSet<CustomerPayment> Set => _context.Set<CustomerPayment>();

    public async Task<CustomerPayment> AddAsync(CustomerPayment entity)
    {
        entity.CreatedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        await Set.AddAsync(entity);
        await _context.SaveChangesAsync();
        return entity;
    }

    public async Task<CustomerPayment> UpdateAsync(int id, CustomerPayment entity)
    {
        var existing = await Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"Entity with id {id} was not found.");

        existing.CustomerFuelGivenId = entity.CustomerFuelGivenId;
        existing.AmountPaid = entity.AmountPaid;
        existing.PaymentDate = entity.PaymentDate;
        existing.BusinessId = entity.BusinessId;
        existing.UserId = entity.UserId;
        existing.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return existing;
    }

    public async Task<CustomerPayment> DeleteAsync(int id)
    {
        var entity = await Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"Entity with id {id} was not found.");

        entity.IsDeleted = true;
        entity.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return entity;
    }

    public Task<List<CustomerPayment>> GetAllAsync() => Set.Where(x => !x.IsDeleted).ToListAsync();

    public Task<CustomerPayment?> GetByIdAsync(int id) =>
        Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);

    public async Task<PagedResult<CustomerPayment>> GetPagedAsync(int page, int pageSize, string? search, int? businessId)
    {
        var q = Set.AsQueryable().Where(x => !x.IsDeleted);
        if (businessId.HasValue) q = q.Where(x => x.BusinessId == businessId.Value);
        if (!string.IsNullOrWhiteSpace(search) && int.TryParse(search.Trim(), out var n))
        {
            q = q.Where(x => x.Id == n || x.CustomerFuelGivenId == n || x.UserId == n);
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 500);
        var total = await q.CountAsync();
        var items = await q
            .OrderByDescending(x => x.PaymentDate)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var userIds = items.Select(x => x.UserId).Distinct().ToList();
        if (userIds.Count > 0)
        {
            var names = await _context.Set<User>()
                .AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, u.Name })
                .ToListAsync();
            var map = names.ToDictionary(x => x.Id, x => x.Name);
            foreach (var p in items)
                p.UserName = map.GetValueOrDefault(p.UserId);
        }

        var givenIds = items.Select(x => x.CustomerFuelGivenId).Distinct().ToList();
        if (givenIds.Count > 0)
        {
            var dues = await _context.Set<CustomerFuelGiven>()
                .AsNoTracking()
                .Where(g => givenIds.Contains(g.Id) && !g.IsDeleted)
                .Select(g => new { g.Id, Due = g.GivenLiter * g.Price })
                .ToListAsync();
            var dueMap = dues.ToDictionary(x => x.Id, x => x.Due);

            var paidSums = await Set.AsNoTracking()
                .Where(x => !x.IsDeleted && givenIds.Contains(x.CustomerFuelGivenId))
                .GroupBy(x => x.CustomerFuelGivenId)
                .Select(g => new { GivenId = g.Key, Total = g.Sum(x => x.AmountPaid) })
                .ToListAsync();
            var paidMap = paidSums.ToDictionary(x => x.GivenId, x => x.Total);

            foreach (var p in items)
            {
                if (!dueMap.TryGetValue(p.CustomerFuelGivenId, out var due))
                {
                    p.RemainingBalance = null;
                    p.PaymentStatus = "—";
                    continue;
                }

                var paid = paidMap.GetValueOrDefault(p.CustomerFuelGivenId);
                var remaining = due - paid;
                if (remaining < 0)
                    remaining = 0;
                p.RemainingBalance = Math.Round(remaining, 2, MidpointRounding.AwayFromZero);

                if (paid < 0.0001)
                    p.PaymentStatus = "Unpaid";
                else if (p.RemainingBalance < 0.0001)
                    p.PaymentStatus = "Paid";
                else
                    p.PaymentStatus = "Half-paid";
            }
        }

        return new PagedResult<CustomerPayment>(items, total, page, pageSize);
    }
}

