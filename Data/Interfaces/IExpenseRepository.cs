using gas_station.Common;
using gas_station.Models;

namespace gas_station.Data.Interfaces;

public interface IExpenseRepository : IGasStationInterface<Expense>
{
    Task<List<Expense>> GetAllAsync();
    Task<Expense?> GetByIdAsync(int id);
    Task<PagedResult<Expense>> GetPagedAsync(int page, int pageSize, string? search, int? businessId, int? stationId = null, string? type = null, string? sideAction = null);
}
