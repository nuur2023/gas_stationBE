using gas_station.Common;
using gas_station.Models;

namespace gas_station.Data.Interfaces;

public interface IMenuRepository : IGasStationInterface<Menu>
{
    Task<List<Menu>> GetAllAsync();
    Task<List<Menu>> GetTreeAsync();
    Task<Menu?> GetByIdAsync(int id);
    Task<PagedResult<Menu>> GetPagedAsync(int page, int pageSize, string? search);
}
