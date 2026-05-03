using gas_station.Common;
using gas_station.Data.Context;
using gas_station.Data.Interfaces;
using gas_station.Models;
using gas_station.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace gas_station.Data.Repository;

public class NotificationsRepository(GasStationDBContext context) : INotificationsRepository
{
    private readonly GasStationDBContext _context = context;

    private IQueryable<AppNotification> BaseNotifications(int businessId, int? stationIdFilter, bool isSuperAdmin)
    {
        var q = _context.AppNotifications.AsNoTracking().Where(n => !n.IsDeleted && n.BusinessId == businessId);
        if (!isSuperAdmin && stationIdFilter is > 0)
            q = q.Where(n => n.StationId == stationIdFilter.Value);
        return q;
    }

    public async Task<PagedResult<AppNotificationDto>> GetPagedForUserAsync(
        int businessId,
        int userId,
        int? stationIdFilter,
        bool isSuperAdmin,
        int page,
        int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var baseN = BaseNotifications(businessId, stationIdFilter, isSuperAdmin);

        var q =
            from n in baseN
            join st in _context.Stations.AsNoTracking() on n.StationId equals st.Id
            where !st.IsDeleted
            join read in _context.AppNotificationReads.AsNoTracking()
                on new { Aid = n.Id, UserId = userId } equals new { Aid = read.AppNotificationId, read.UserId } into readj
            from read in readj.DefaultIfEmpty()
            join cu in _context.Users.AsNoTracking() on n.ConfirmedByUserId equals cu.Id into cuj
            from cu in cuj.DefaultIfEmpty()
            join t in _context.TransferInventories.AsNoTracking() on n.TransferInventoryId equals t.Id into tj
            from t in tj.DefaultIfEmpty()
            join b in _context.BusinessFuelInventories.AsNoTracking() on t.BusinessFuelInventoryId equals b.Id into bj
            from b in bj.DefaultIfEmpty()
            join f in _context.FuelTypes.AsNoTracking() on b.FuelTypeId equals f.Id into fj
            from f in fj.DefaultIfEmpty()
            orderby n.CreatedAt descending, n.Id descending
            select new
            {
                n,
                st.Name,
                ConfirmedByName = cu != null ? cu.Name : null,
                Liters = t != null ? t.Liters : 0.0,
                FuelName = f != null ? f.FuelName : string.Empty,
                TransferDate = t != null ? t.Date : n.CreatedAt,
                IsRead = read != null,
            };

        var total = await q.CountAsync();
        var rows = await q
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var items = rows.Select(x => new AppNotificationDto
        {
            Id = x.n.Id,
            BusinessId = x.n.BusinessId,
            StationId = x.n.StationId,
            StationName = x.Name,
            Title = x.n.Title,
            Body = x.n.Body,
            CreatedAt = x.n.CreatedAt,
            IsRead = x.IsRead,
            TransferInventoryId = x.n.TransferInventoryId,
            ConfirmedByName = x.ConfirmedByName,
            Liters = x.Liters,
            FuelName = x.FuelName,
            TransferDate = x.TransferDate,
        }).ToList();

        return new PagedResult<AppNotificationDto>(items, total, page, pageSize);
    }

    public async Task<int> CountUnreadAsync(int businessId, int userId, int? stationIdFilter, bool isSuperAdmin)
    {
        var baseN = BaseNotifications(businessId, stationIdFilter, isSuperAdmin);
        return await baseN
            .Where(n => !_context.AppNotificationReads.Any(r => r.AppNotificationId == n.Id && r.UserId == userId))
            .CountAsync();
    }

    public async Task<bool> MarkReadAsync(int notificationId, int businessId, int userId, int? stationIdFilter, bool isSuperAdmin)
    {
        var n = await _context.AppNotifications.FirstOrDefaultAsync(x =>
            x.Id == notificationId && !x.IsDeleted && x.BusinessId == businessId);
        if (n is null) return false;
        if (!isSuperAdmin && stationIdFilter is > 0 && n.StationId != stationIdFilter.Value) return false;

        if (await _context.AppNotificationReads.AnyAsync(r => r.AppNotificationId == notificationId && r.UserId == userId))
            return true;

        _context.AppNotificationReads.Add(new AppNotificationRead
        {
            AppNotificationId = notificationId,
            UserId = userId,
            ReadAtUtc = DateTime.UtcNow,
        });
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<int> MarkAllReadAsync(int businessId, int userId, int? stationIdFilter, bool isSuperAdmin)
    {
        var baseN = BaseNotifications(businessId, stationIdFilter, isSuperAdmin);
        var unreadIds = await baseN
            .Where(n => !_context.AppNotificationReads.Any(r => r.AppNotificationId == n.Id && r.UserId == userId))
            .Select(n => n.Id)
            .ToListAsync();

        var now = DateTime.UtcNow;
        foreach (var id in unreadIds)
        {
            _context.AppNotificationReads.Add(new AppNotificationRead
            {
                AppNotificationId = id,
                UserId = userId,
                ReadAtUtc = now,
            });
        }

        if (unreadIds.Count > 0)
            await _context.SaveChangesAsync();

        return unreadIds.Count;
    }
}
