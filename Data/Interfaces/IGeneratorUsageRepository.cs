using gas_station.Common;
using gas_station.Models;

namespace gas_station.Data.Interfaces;

public interface IGeneratorUsageRepository : IGasStationInterface<GeneratorUsage>
{
    Task<List<GeneratorUsage>> GetAllAsync();
    Task<GeneratorUsage?> GetByIdAsync(int id);
    Task<PagedResult<GeneratorUsage>> GetPagedAsync(int page, int pageSize, string? search, int? businessId, int? stationId = null);
}
