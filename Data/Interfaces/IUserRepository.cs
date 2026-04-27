using backend.Common;
using backend.Models;

namespace backend.Data.Interfaces;

public interface IUserRepository : IGasStationInterface<User>
{
    Task<List<User>> GetAllAsync();
    Task<User?> GetByIdAsync(int id);
    Task<PagedResult<User>> GetPagedAsync(int page, int pageSize, string? search, int? businessId = null, bool includeElevatedRoles = true);
}
