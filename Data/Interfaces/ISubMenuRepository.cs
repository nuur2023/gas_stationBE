using gas_station.Common;
using gas_station.Models;

namespace gas_station.Data.Interfaces;

public interface ISubMenuRepository : IGasStationInterface<SubMenu>
{
    Task<List<SubMenu>> GetAllAsync();
    Task<SubMenu?> GetByIdAsync(int id);
    Task<PagedResult<SubMenu>> GetPagedAsync(int page, int pageSize, string? search);
}
