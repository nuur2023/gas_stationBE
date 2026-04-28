using gas_station.Common;
using gas_station.Models;

namespace gas_station.Data.Interfaces;

public interface IJournalEntryRepository : IGasStationInterface<JournalEntry>
{
    Task<List<JournalEntry>> GetAllAsync();
    Task<JournalEntry?> GetByIdAsync(int id);
    Task<PagedResult<JournalEntry>> GetPagedAsync(int page, int pageSize, string? search, int? businessId, int? filterStationId);
}

