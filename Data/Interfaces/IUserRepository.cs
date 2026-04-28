using gas_station.Common;
using gas_station.Models;

namespace gas_station.Data.Interfaces;

public interface IUserRepository : IGasStationInterface<User>
{
    Task<List<User>> GetAllAsync();
    Task<User?> GetByIdAsync(int id);
    Task<PagedResult<User>> GetPagedAsync(int page, int pageSize, string? search, int? businessId = null, bool includeElevatedRoles = true);
}
