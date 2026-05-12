using System.Globalization;
using gas_station.Common;
using gas_station.Data.Context;
using gas_station.Data.Interfaces;
using gas_station.Models;
using Microsoft.EntityFrameworkCore;

namespace gas_station.Data.Repository;

public class CustomerPaymentRepository(GasStationDBContext context) : ICustomerPaymentRepository
{
    private readonly GasStationDBContext _context = context;
    private DbSet<CustomerPayment> Set => _context.Set<CustomerPayment>();

    public async Task<CustomerPayment> AddAsync(CustomerPayment entity)
    {
        var now = DateTime.UtcNow;
        entity.CreatedAt = now;
        entity.UpdatedAt = now;
        entity.IsDeleted = false;
        await Set.AddAsync(entity);
        await _context.SaveChangesAsync();
        return entity;
    }

    public async Task<CustomerPayment> UpdateAsync(int id, CustomerPayment entity)
    {
        var existing = await Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"Entity with id {id} was not found.");

        existing.CustomerId = entity.CustomerId;
        existing.ReferenceNo = entity.ReferenceNo;
        existing.Description = entity.Description;
        existing.ChargedAmount = entity.ChargedAmount;
        existing.AmountPaid = entity.AmountPaid;
        existing.Balance = entity.Balance;
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
        Set.Include(x => x.Customer).FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);

    public async Task<PagedResult<CustomerPayment>> GetPagedAsync(int page, int pageSize, string? search, int? businessId, int? filterStationId = null)
    {
        var q = Set.Include(x => x.Customer).AsQueryable().Where(x => !x.IsDeleted);
        if (businessId.HasValue) q = q.Where(x => x.BusinessId == businessId.Value);
        if (filterStationId is > 0) q = q.Where(x => x.Customer.StationId == filterStationId.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            if (int.TryParse(s, out var n))
            {
                q = q.Where(x =>
                    x.Id == n ||
                    x.CustomerId == n ||
                    x.UserId == n ||
                    EF.Functions.Like(x.Customer.Name, $"%{s}%") ||
                    EF.Functions.Like(x.Customer.Phone, $"%{s}%"));
            }
            else
            {
                q = q.Where(x =>
                    EF.Functions.Like(x.Customer.Name, $"%{s}%") ||
                    EF.Functions.Like(x.Customer.Phone, $"%{s}%") ||
                    (x.ReferenceNo != null && EF.Functions.Like(x.ReferenceNo, $"%{s}%")));
            }
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

        // Customer ledger status uses (BusinessId, CustomerId).
        var keys = items
            .Select(x => new { x.BusinessId, x.CustomerId })
            .Distinct()
            .ToList();
        if (keys.Count > 0)
        {
            var balances = new Dictionary<(int, int), (double Charged, double Paid)>();
            foreach (var k in keys)
            {
                var rows = await Set.AsNoTracking()
                    .Where(x => !x.IsDeleted
                                && x.BusinessId == k.BusinessId
                                && x.CustomerId == k.CustomerId)
                    .Select(x => new { x.ChargedAmount, x.AmountPaid })
                    .ToListAsync();
                var charged = rows.Sum(r => r.ChargedAmount);
                var paid = rows.Sum(r => r.AmountPaid);
                balances[(k.BusinessId, k.CustomerId)] = (charged, paid);
            }

            foreach (var p in items)
            {
                if (!balances.TryGetValue((p.BusinessId, p.CustomerId), out var t))
                {
                    p.RemainingBalance = null;
                    p.PaymentStatus = "—";
                    continue;
                }

                var remaining = t.Charged - t.Paid;
                if (remaining < 0)
                    remaining = 0;
                p.RemainingBalance = Math.Round(remaining, 2, MidpointRounding.AwayFromZero);

                // Per-row status: payment lines are "Paid"; charged/debt lines stay "Unpaid" until fully settled.
                var isPaymentLine = p.AmountPaid > 0.0001
                    && string.Equals(p.Description, "Payment", StringComparison.OrdinalIgnoreCase);
                var isChargedLine = p.ChargedAmount > 0.0001
                    || string.Equals(p.Description, "Charged", StringComparison.OrdinalIgnoreCase);

                if (isPaymentLine && p.ChargedAmount <= 0.0001)
                {
                    p.PaymentStatus = "Paid";
                }
                else if (isChargedLine)
                {
                    if (p.RemainingBalance < 0.0001)
                    {
                        p.PaymentStatus = "Paid";
                    }
                    else
                    {
                        p.PaymentStatus = "Unpaid";
                    }
                   
                }
                else
                {
                    p.PaymentStatus = "—";
                }
            }
        }

        return new PagedResult<CustomerPayment>(items, total, page, pageSize);
    }

    public async Task<double> GetCustomerBalanceAsync(int businessId, int customerId)
    {
        if (businessId <= 0 || customerId <= 0) return 0;
        var rows = Set.AsNoTracking()
            .Where(x => !x.IsDeleted
                        && x.BusinessId == businessId
                        && x.CustomerId == customerId);
        return await rows.SumAsync(x => x.ChargedAmount - x.AmountPaid);
    }

    public async Task<string> GenerateReferenceAsync(int businessId, DateTime date)
    {
        var dayKey = date.Date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var prefix = $"CP-{businessId}-{dayKey}-";
        var existingForDay = await Set.AsNoTracking()
            .Where(x => x.BusinessId == businessId
                        && x.ReferenceNo != null
                        && EF.Functions.Like(x.ReferenceNo, prefix + "%"))
            .Select(x => x.ReferenceNo!)
            .ToListAsync();

        var maxSeq = 0;
        foreach (var r in existingForDay)
        {
            var tail = r[prefix.Length..];
            if (int.TryParse(tail, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > maxSeq)
                maxSeq = n;
        }

        return $"{prefix}{(maxSeq + 1):0000}";
    }

    public async Task SyncCustomerChargedTotalAndRecalculateBalancesAsync(int businessId, int customerId, int actingUserId)
    {
        if (businessId <= 0 || customerId <= 0) return;
        var customer = await _context.Customers.AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == customerId && x.BusinessId == businessId);
        if (customer is null) return;

        var txs = await _context.CustomerFuelGivens.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BusinessId == businessId && x.CustomerId == customerId)
            .ToListAsync();
        var charged = txs.Sum(ChargedFromCfg);
        var latestDate = txs.Count > 0 ? txs.Max(x => x.Date) : DateTime.UtcNow;

        var ledger = await Set
            .FirstOrDefaultAsync(x => !x.IsDeleted
                                      && x.BusinessId == businessId
                                      && x.CustomerId == customerId
                                      && x.Description == "Charged");
        if (ledger is null)
        {
            if (actingUserId <= 0)
                throw new ArgumentOutOfRangeException(nameof(actingUserId), "A valid user id is required to create a customer payment ledger row.");
            var now = DateTime.UtcNow;
            ledger = new CustomerPayment
            {
                CustomerId = customerId,
                ReferenceNo = await GenerateReferenceAsync(businessId, latestDate),
                Description = "Charged",
                ChargedAmount = charged,
                AmountPaid = 0,
                Balance = 0,
                PaymentDate = latestDate,
                BusinessId = businessId,
                UserId = actingUserId,
                CreatedAt = now,
                UpdatedAt = now,
                IsDeleted = false,
            };
            await Set.AddAsync(ledger);
        }
        else
        {
            ledger.ChargedAmount = charged;
            ledger.PaymentDate = latestDate;
            ledger.UpdatedAt = DateTime.UtcNow;
            if (actingUserId > 0)
                ledger.UserId = actingUserId;
        }
        await _context.SaveChangesAsync();
        await RecalculateCustomerBalancesAsync(businessId, customerId);
    }

    public async Task RecalculateCustomerBalancesAsync(int businessId, int customerId)
    {
        if (businessId <= 0 || customerId <= 0) return;

        var rows = await Set
            .Where(x => !x.IsDeleted
                        && x.BusinessId == businessId
                        && x.CustomerId == customerId)
            .OrderBy(x => x.PaymentDate)
            .ThenBy(x => x.Id)
            .ToListAsync();

        double running = 0;
        foreach (var r in rows)
        {
            running += r.ChargedAmount - r.AmountPaid;
            r.Balance = Math.Round(running, 2, MidpointRounding.AwayFromZero);
            r.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();
    }

    /// <summary>Charged amount that a customer fuel transaction contributes to the ledger.</summary>
    public static double ChargedFromCfg(CustomerFuelTransaction cfg)
    {
        if (string.Equals(cfg.Type, "Cash", StringComparison.OrdinalIgnoreCase))
            return Math.Round(cfg.CashAmount, 2, MidpointRounding.AwayFromZero);
        return Math.Round(cfg.GivenLiter * cfg.Price, 2, MidpointRounding.AwayFromZero);
    }
}
