using backend.Common;
using backend.Models;

namespace backend.Data.Interfaces;

public interface IStationRepository : IGasStationInterface<Station>
{
    Task<Station?> GetByIdAsync(int id);
    /// <summary><paramref name="businessId"/> null = no filter (all businesses).</summary>
    Task<PagedResult<Station>> GetPagedAsync(int page, int pageSize, string? search, int? businessId);
}
