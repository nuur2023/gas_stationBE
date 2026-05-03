using gas_station.Common;
using gas_station.ViewModels;

namespace gas_station.Data.Interfaces;

public interface INotificationsRepository
{
    Task<PagedResult<AppNotificationDto>> GetPagedForUserAsync(
        int businessId,
        int userId,
        int? stationIdFilter,
        bool isSuperAdmin,
        int page,
        int pageSize);

    Task<int> CountUnreadAsync(int businessId, int userId, int? stationIdFilter, bool isSuperAdmin);

    Task<bool> MarkReadAsync(int notificationId, int businessId, int userId, int? stationIdFilter, bool isSuperAdmin);

    Task<int> MarkAllReadAsync(int businessId, int userId, int? stationIdFilter, bool isSuperAdmin);
}
