using backend.Common;
using backend.Models;
using backend.ViewModels;

namespace backend.Data.Interfaces;

public interface IRateRepository : IGasStationInterface<Rate>
{
    Task<List<Rate>> GetAllAsync();
    Task<Rate?> GetByIdAsync(int id);
    Task<PagedResult<RateViewModel>> GetPagedAsync(int page, int pageSize, string? search, int? businessId);
}
