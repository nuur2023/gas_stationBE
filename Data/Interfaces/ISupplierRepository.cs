using backend.Common;
using backend.Models;

namespace backend.Data.Interfaces;

public interface ISupplierRepository : IGasStationInterface<Supplier>
{
    Task<Supplier?> GetByIdAsync(int id);
    Task<PagedResult<Supplier>> GetPagedAsync(int page, int pageSize, string? search, int? businessId);
}
