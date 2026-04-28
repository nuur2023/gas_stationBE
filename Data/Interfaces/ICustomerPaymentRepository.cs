using gas_station.Common;
using gas_station.Models;

namespace gas_station.Data.Interfaces;

public interface ICustomerPaymentRepository : IGasStationInterface<CustomerPayment>
{
    Task<List<CustomerPayment>> GetAllAsync();
    Task<CustomerPayment?> GetByIdAsync(int id);
    Task<PagedResult<CustomerPayment>> GetPagedAsync(int page, int pageSize, string? search, int? businessId);
}

