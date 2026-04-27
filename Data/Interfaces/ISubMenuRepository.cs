using backend.Common;
using backend.Models;

namespace backend.Data.Interfaces;

public interface ISubMenuRepository : IGasStationInterface<SubMenu>
{
    Task<List<SubMenu>> GetAllAsync();
    Task<SubMenu?> GetByIdAsync(int id);
    Task<PagedResult<SubMenu>> GetPagedAsync(int page, int pageSize, string? search);
}
