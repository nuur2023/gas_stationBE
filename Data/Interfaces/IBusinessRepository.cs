using backend.Common;
using backend.Models;

namespace backend.Data.Interfaces;

public interface IBusinessRepository : IGasStationInterface<Business>
{
    Task<List<Business>> GetAllAsync();
    Task<Business?> GetByIdAsync(int id);
    Task<PagedResult<Business>> GetPagedAsync(int page, int pageSize, string? search);
}
