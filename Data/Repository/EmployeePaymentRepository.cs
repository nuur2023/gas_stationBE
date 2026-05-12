using System.Globalization;
using gas_station.Common;
using gas_station.Data.Context;
using gas_station.Data.Interfaces;
using gas_station.Models;
using Microsoft.EntityFrameworkCore;

namespace gas_station.Data.Repository;

public class EmployeePaymentRepository(GasStationDBContext context) : IEmployeePaymentRepository
{
    private readonly GasStationDBContext _context = context;
    private DbSet<EmployeePayment> Set => _context.Set<EmployeePayment>();

    public async Task<EmployeePayment> AddAsync(EmployeePayment entity)
    {
        var now = DateTime.UtcNow;
        entity.CreatedAt = now;
        entity.UpdatedAt = now;
        entity.IsDeleted = false;
        await Set.AddAsync(entity);
        await _context.SaveChangesAsync();
        return entity;
    }

    public async Task<EmployeePayment> UpdateAsync(int id, EmployeePayment entity)
    {
        var existing = await Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"Entity with id {id} was not found.");

        existing.EmployeeId = entity.EmployeeId;
        existing.ReferenceNo = entity.ReferenceNo;
        existing.Description = entity.Description;
        existing.ChargedAmount = entity.ChargedAmount;
        existing.PaidAmount = entity.PaidAmount;
        existing.Balance = entity.Balance;
        existing.PaymentDate = entity.PaymentDate;
        existing.PeriodLabel = entity.PeriodLabel;
        existing.BusinessId = entity.BusinessId;
        existing.UserId = entity.UserId;
        existing.StationId = entity.StationId;
        existing.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return existing;
    }

    public async Task<EmployeePayment> DeleteAsync(int id)
    {
        var entity = await Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"Entity with id {id} was not found.");

        entity.IsDeleted = true;
        entity.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return entity;
    }

    public Task<EmployeePayment?> GetByIdAsync(int id) =>
        Set.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);

    public async Task<PagedResult<EmployeePayment>> GetPagedAsync(
        int page,
        int pageSize,
        string? search,
        int? businessId,
        int? stationId,
        int? employeeId,
        string? period)
    {
        var q = Set.AsQueryable().Where(x => !x.IsDeleted);
        if (businessId.HasValue) q = q.Where(x => x.BusinessId == businessId.Value);
        if (stationId.HasValue) q = q.Where(x => x.StationId == stationId.Value);
        if (employeeId.HasValue) q = q.Where(x => x.EmployeeId == employeeId.Value);
        if (!string.IsNullOrWhiteSpace(period))
        {
            var p = period.Trim();
            q = q.Where(x => x.PeriodLabel == p);
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            if (int.TryParse(s, out var n))
            {
                q = q.Where(x => x.Id == n || x.EmployeeId == n);
            }
            else
            {
                q = q.Where(x =>
                    (x.ReferenceNo != null && EF.Functions.Like(x.ReferenceNo, $"%{s}%")) ||
                    EF.Functions.Like(x.Description, $"%{s}%"));
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

        // Enrich with user + employee names so the frontend doesn't have to lookup each row.
        var userIds = items.Select(x => x.UserId).Distinct().ToList();
        if (userIds.Count > 0)
        {
            var names = await _context.Set<User>().AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, u.Name })
                .ToListAsync();
            var map = names.ToDictionary(x => x.Id, x => x.Name);
            foreach (var p in items) p.UserName = map.GetValueOrDefault(p.UserId);
        }

        var employeeIds = items.Select(x => x.EmployeeId).Distinct().ToList();
        if (employeeIds.Count > 0)
        {
            var emps = await _context.Set<Employee>().AsNoTracking()
                .Where(e => employeeIds.Contains(e.Id))
                .Select(e => new { e.Id, e.Name })
                .ToListAsync();
            var map = emps.ToDictionary(x => x.Id, x => x.Name);
            foreach (var p in items) p.EmployeeName = map.GetValueOrDefault(p.EmployeeId);
        }

        // Per-employee outstanding totals (within the same business as each row).
        var keys = items.Select(x => new { x.BusinessId, x.EmployeeId }).Distinct().ToList();
        if (keys.Count > 0)
        {
            var totals = new Dictionary<(int, int), (double Charged, double Paid)>();
            foreach (var k in keys)
            {
                var rows = await Set.AsNoTracking()
                    .Where(x => !x.IsDeleted
                                && x.BusinessId == k.BusinessId
                                && x.EmployeeId == k.EmployeeId)
                    .Select(x => new { x.ChargedAmount, x.PaidAmount })
                    .ToListAsync();
                totals[(k.BusinessId, k.EmployeeId)] = (rows.Sum(r => r.ChargedAmount), rows.Sum(r => r.PaidAmount));
            }
            foreach (var p in items)
            {
                if (totals.TryGetValue((p.BusinessId, p.EmployeeId), out var t))
                {
                    var remaining = t.Charged - t.Paid;
                    if (remaining < 0) remaining = 0;
                    p.RemainingBalance = Math.Round(remaining, 2, MidpointRounding.AwayFromZero);
                }
            }
        }

        return new PagedResult<EmployeePayment>(items, total, page, pageSize);
    }

    public async Task<double> GetEmployeeBalanceAsync(int businessId, int employeeId)
    {
        if (businessId <= 0 || employeeId <= 0) return 0;
        var rows = Set.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BusinessId == businessId && x.EmployeeId == employeeId);
        return await rows.SumAsync(x => x.ChargedAmount - x.PaidAmount);
    }

    public async Task<string> GenerateReferenceAsync(int businessId, DateTime date)
    {
        var dayKey = date.Date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var prefix = $"EP-{businessId}-{dayKey}-";
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

    public async Task RecalculateEmployeeBalancesAsync(int businessId, int employeeId)
    {
        if (businessId <= 0 || employeeId <= 0) return;
        var rows = await Set
            .Where(x => !x.IsDeleted && x.BusinessId == businessId && x.EmployeeId == employeeId)
            .OrderBy(x => x.PaymentDate)
            .ThenBy(x => x.Id)
            .ToListAsync();

        double running = 0;
        foreach (var r in rows)
        {
            running += r.ChargedAmount - r.PaidAmount;
            r.Balance = Math.Round(running, 2, MidpointRounding.AwayFromZero);
            r.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();
    }

    public async Task<List<EmployeePayment>> CreatePayrollRunAsync(
        int businessId,
        int? stationId,
        int userId,
        string period,
        DateTime paymentDate,
        IReadOnlyList<(int EmployeeId, double Charged, double Paid)> items)
    {
        var created = new List<EmployeePayment>();
        if (items.Count == 0) return created;

        var trimmedPeriod = (period ?? string.Empty).Trim();
        var now = DateTime.UtcNow;
        var dayKey = paymentDate.Date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var prefix = $"EP-{businessId}-{dayKey}-";
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

        foreach (var item in items)
        {
            // Salary accrual row (charge).
            if (item.Charged > 0)
            {
                maxSeq++;
                var charge = new EmployeePayment
                {
                    EmployeeId = item.EmployeeId,
                    ReferenceNo = $"{prefix}{maxSeq:0000}",
                    Description = "Salary",
                    ChargedAmount = Math.Round(item.Charged, 2, MidpointRounding.AwayFromZero),
                    PaidAmount = 0,
                    Balance = 0,
                    PaymentDate = paymentDate,
                    PeriodLabel = trimmedPeriod,
                    BusinessId = businessId,
                    UserId = userId,
                    StationId = stationId,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                await Set.AddAsync(charge);
                created.Add(charge);
            }

            // Payment row.
            if (item.Paid > 0)
            {
                maxSeq++;
                var payment = new EmployeePayment
                {
                    EmployeeId = item.EmployeeId,
                    ReferenceNo = $"{prefix}{maxSeq:0000}",
                    Description = "Payment",
                    ChargedAmount = 0,
                    PaidAmount = Math.Round(item.Paid, 2, MidpointRounding.AwayFromZero),
                    Balance = 0,
                    PaymentDate = paymentDate,
                    PeriodLabel = trimmedPeriod,
                    BusinessId = businessId,
                    UserId = userId,
                    StationId = stationId,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                await Set.AddAsync(payment);
                created.Add(payment);
            }
        }

        await _context.SaveChangesAsync();

        // Recalc per affected employee so Balance snapshots stay in sync.
        var affected = items.Select(i => i.EmployeeId).Distinct().ToList();
        foreach (var eid in affected)
            await RecalculateEmployeeBalancesAsync(businessId, eid);

        return created;
    }
}
