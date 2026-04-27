using backend.Common;
using backend.Models;

namespace backend.Data.Interfaces;

public interface IExpenseRepository : IGasStationInterface<Expense>
{
    Task<List<Expense>> GetAllAsync();
    Task<Expense?> GetByIdAsync(int id);
    Task<PagedResult<Expense>> GetPagedAsync(int page, int pageSize, string? search, int? businessId, int? stationId = null);
}
