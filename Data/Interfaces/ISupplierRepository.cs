using gas_station.Common;
using gas_station.Models;

namespace gas_station.Data.Interfaces;

public interface ISupplierRepository : IGasStationInterface<Supplier>
{
    Task<Supplier?> GetByIdAsync(int id);
    Task<PagedResult<Supplier>> GetPagedAsync(int page, int pageSize, string? search, int? businessId);
}
