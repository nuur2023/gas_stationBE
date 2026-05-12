using gas_station.Common;
using gas_station.Models;

namespace gas_station.Data.Interfaces;

public interface IEmployeeRepository : IGasStationInterface<Employee>
{
    Task<Employee?> GetByIdAsync(int id);
    Task<PagedResult<Employee>> GetPagedAsync(
        int page,
        int pageSize,
        string? search,
        int? businessId,
        int? stationId,
        bool includeInactive);

    /// <summary>Lightweight active-employee picker for payroll runs and report selectors.</summary>
    Task<List<Employee>> GetActiveAsync(int businessId, int? stationId);
}
