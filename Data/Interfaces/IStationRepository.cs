using gas_station.Common;
using gas_station.Models;

namespace gas_station.Data.Interfaces;

public interface IStationRepository : IGasStationInterface<Station>
{
    Task<Station?> GetByIdAsync(int id);
    /// <summary><paramref name="businessId"/> null = no filter (all businesses).</summary>
    Task<PagedResult<Station>> GetPagedAsync(int page, int pageSize, string? search, int? businessId);
}
