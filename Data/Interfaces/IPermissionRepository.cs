using backend.Common;
using backend.Models;

namespace backend.Data.Interfaces;

public interface IPermissionRepository : IGasStationInterface<Permission>
{
    Task<List<Permission>> GetAllAsync();
    Task<List<Permission>> GetByUserAndBusinessAsync(int userId, int businessId);
    Task ReplaceForUserAndBusinessAsync(int userId, int businessId, IReadOnlyList<Permission> newPermissions);
    Task<Permission?> GetByIdAsync(int id);
    Task<PagedResult<Permission>> GetPagedAsync(int page, int pageSize, string? search);
}
