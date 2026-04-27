using backend.Common;
using backend.Models;

namespace backend.Data.Interfaces;

public interface ICustomerPaymentRepository : IGasStationInterface<CustomerPayment>
{
    Task<List<CustomerPayment>> GetAllAsync();
    Task<CustomerPayment?> GetByIdAsync(int id);
    Task<PagedResult<CustomerPayment>> GetPagedAsync(int page, int pageSize, string? search, int? businessId);
}

