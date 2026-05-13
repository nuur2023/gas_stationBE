using gas_station.ViewModels;

namespace gas_station.Data.Interfaces;

public interface IAccountingDashboardRepository
{
    Task<AccountingDashboardOverviewDto> GetOverviewAsync(int businessId, int? stationId, CancellationToken cancellationToken = default);

    Task<AccountingDashboardRecentTransactionsPagedDto> GetRecentTransactionsPagedAsync(
        int businessId,
        int? stationId,
        DateTime fromDate,
        DateTime toDate,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}
