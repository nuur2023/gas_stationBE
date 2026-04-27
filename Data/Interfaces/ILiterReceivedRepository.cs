using backend.Common;
using backend.Models;

namespace backend.Data.Interfaces;

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
