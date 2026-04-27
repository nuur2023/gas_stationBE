using backend.Common;
using backend.Models;

namespace backend.Data.Interfaces;

public interface IGeneratorUsageRepository : IGasStationInterface<GeneratorUsage>
{
    Task<List<GeneratorUsage>> GetAllAsync();
    Task<GeneratorUsage?> GetByIdAsync(int id);
    Task<PagedResult<GeneratorUsage>> GetPagedAsync(int page, int pageSize, string? search, int? businessId, int? stationId = null);
}
