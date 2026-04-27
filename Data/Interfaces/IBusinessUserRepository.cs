using backend.Common;
using backend.Models;

namespace backend.Data.Interfaces;

public interface IBusinessUserRepository : IGasStationInterface<BusinessUser>
{
    Task<List<BusinessUser>> GetAllAsync();
    Task<BusinessUser?> GetByIdAsync(int id);
    Task<PagedResult<BusinessUser>> GetPagedAsync(int page, int pageSize, string? search, int? businessId = null, bool includeElevatedRoles = true);

    /// <summary>True if a non-deleted link exists for this user, business, and station (excluding <paramref name="excludeId"/> when set).</summary>
    Task<bool> LinkExistsAsync(int userId, int businessId, int stationId, int? excludeId = null);
}
