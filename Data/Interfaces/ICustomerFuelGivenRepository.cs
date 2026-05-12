using gas_station.Common;
using gas_station.Models;

namespace gas_station.Data.Interfaces;

public interface ICustomerFuelGivenRepository : IGasStationInterface<CustomerFuelTransaction>
{
    Task<List<CustomerFuelTransaction>> GetAllAsync();
    Task<CustomerFuelTransaction?> GetByIdAsync(int id);
    Task<PagedResult<CustomerFuelTransaction>> GetPagedAsync(int page, int pageSize, string? search, int? businessId, int? stationId = null);
}

