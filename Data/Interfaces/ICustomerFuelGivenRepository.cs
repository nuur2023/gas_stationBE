using backend.Common;
using backend.Models;

namespace backend.Data.Interfaces;

public interface ICustomerFuelGivenRepository : IGasStationInterface<CustomerFuelGiven>
{
    Task<List<CustomerFuelGiven>> GetAllAsync();
    Task<CustomerFuelGiven?> GetByIdAsync(int id);
    Task<PagedResult<CustomerFuelGiven>> GetPagedAsync(int page, int pageSize, string? search, int? businessId, int? stationId = null);
}

