using gas_station.Common;
using gas_station.Models;

namespace gas_station.Data.Interfaces;

public interface ILiterReceivedRepository : IGasStationInterface<LiterReceived>
{
    Task<LiterReceived?> GetByIdAsync(int id);
    Task<PagedResult<LiterReceived>> GetPagedAsync(
        int page,
        int pageSize,
        string? search,
        int? businessId,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        int? stationId = null);
}
