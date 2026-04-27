using backend.Common;
using backend.Models;

namespace backend.Data.Interfaces;

public interface IRoleRepository : IGasStationInterface<Role>
{
    Task<List<Role>> GetAllAsync();
    Task<Role?> GetByIdAsync(int id);
    Task<PagedResult<Role>> GetPagedAsync(int page, int pageSize, string? search);
}
