using backend.Common;
using backend.Models;

namespace backend.Data.Interfaces;

public interface IJournalEntryRepository : IGasStationInterface<JournalEntry>
{
    Task<List<JournalEntry>> GetAllAsync();
    Task<JournalEntry?> GetByIdAsync(int id);
    Task<PagedResult<JournalEntry>> GetPagedAsync(int page, int pageSize, string? search, int? businessId, int? filterStationId);
}

