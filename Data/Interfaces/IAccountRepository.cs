using backend.Common;
using backend.Models;

namespace backend.Data.Interfaces;

public interface IAccountRepository : IGasStationInterface<Account>
{
    Task<List<Account>> GetAllAsync();
    Task<Account?> GetByIdAsync(int id);
    Task<PagedResult<Account>> GetPagedAsync(int page, int pageSize, string? search, int? businessId);
    Task<Account?> GetByBusinessAndCodeAsync(int? businessId, string code);

    /// <summary>Top-level accounts usable as parents for sub-accounts under <paramref name="businessId"/> (global + that business).</summary>
    Task<List<Account>> GetParentCandidatesAsync(int businessId);
}

