using backend.Common;
using backend.Models;

namespace backend.Data.Interfaces;

public interface IDippingRepository : IGasStationInterface<Dipping>
{
    Task<Dipping?> GetByIdAsync(int id);
    Task<Dipping?> GetFirstByStationAndFuelAsync(int stationId, int fuelTypeId);
    Task<PagedResult<Dipping>> GetPagedAsync(int page, int pageSize, string? search, int? businessId, int? stationId);
}
