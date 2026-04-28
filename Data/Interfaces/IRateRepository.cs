using gas_station.Common;
using gas_station.Models;
using gas_station.ViewModels;

namespace gas_station.Data.Interfaces;

public interface IRateRepository : IGasStationInterface<Rate>
{
    Task<List<Rate>> GetAllAsync();
    Task<Rate?> GetByIdAsync(int id);
    Task<PagedResult<RateViewModel>> GetPagedAsync(int page, int pageSize, string? search, int? businessId);
}
