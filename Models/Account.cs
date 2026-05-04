using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace gas_station.Models;

public class Account : BaseModel
{
    /// <summary>When null, the account is a global (shared) chart parent. Otherwise scoped to that business.</summary>
    public int? BusinessId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public int ChartsOfAccountsId { get; set; }
    [ValidateNever]
    public ChartsOfAccounts ChartsOfAccounts { get; set; } = null!;

    /// <summary>
    /// When <see cref="BusinessId"/> is set and this is null, the row is a business-only staging / temporary
    /// top-level account (not a structural child of the shared chart). It can still receive journal lines;
    /// UI rollups should not treat it as part of normal parent totals.
    /// </summary>
    public int? ParentAccountId { get; set; }
    [ValidateNever]
    public Account? ParentAccount { get; set; }
    [ValidateNever]
    public ICollection<Account>? Children { get; set; }
    [ValidateNever]
    public ICollection<JournalEntryLine> JournalEntryLines { get; set; } = new List<JournalEntryLine>();

}

