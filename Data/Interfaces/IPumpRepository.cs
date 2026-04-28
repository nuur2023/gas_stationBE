using gas_station.Common;
using gas_station.Models;

namespace gas_station.Data.Interfaces;

public interface IPumpRepository : IGasStationInterface<Pump>
{
    Task<List<Pump>> GetAllAsync();
    Task<List<Pump>> GetFilteredAsync(int? dippingId, int? stationId, int? businessId);
    /// <summary><paramref name="businessId"/> null = no filter (SuperAdmin listing all).</summary>
    Task<PagedResult<Pump>> GetPagedAsync(int page, int pageSize, string? search, int? businessId, int? stationId = null);
    Task<Pump?> GetByIdAsync(int id);
}
