using gas_station.Common;
using gas_station.Models;

namespace gas_station.Data.Interfaces;

public interface ISupplierPaymentRepository
{
    Task<PagedResult<SupplierPayment>> GetPagedAsync(int page, int pageSize, string? q, int? businessId);
    Task<SupplierPayment> AddAsync(SupplierPayment entity);
}
