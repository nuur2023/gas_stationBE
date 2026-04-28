using gas_station.Common;
using gas_station.Models;

namespace gas_station.Data.Interfaces;

public interface IBusinessRepository : IGasStationInterface<Business>
{
    Task<List<Business>> GetAllAsync();
    Task<Business?> GetByIdAsync(int id);
    Task<PagedResult<Business>> GetPagedAsync(int page, int pageSize, string? search);
}
