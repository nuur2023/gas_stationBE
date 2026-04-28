using gas_station.Common;
using gas_station.Models;

namespace gas_station.Data.Interfaces;

public interface IRoleRepository : IGasStationInterface<Role>
{
    Task<List<Role>> GetAllAsync();
    Task<Role?> GetByIdAsync(int id);
    Task<PagedResult<Role>> GetPagedAsync(int page, int pageSize, string? search);
}
