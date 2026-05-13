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
    /// Parent in the chart hierarchy; null means a top-level parent for this business (or global when <see cref="BusinessId"/> is null).
    /// Clearing / staging lines belong under the Temporary chart (e.g. as children of Temporary Account), not as orphan top-level rows.
    /// </summary>
    public int? ParentAccountId { get; set; }
    [ValidateNever]
    public Account? ParentAccount { get; set; }
    [ValidateNever]
    public ICollection<Account>? Children { get; set; }
    [ValidateNever]
    public ICollection<JournalEntryLine> JournalEntryLines { get; set; } = new List<JournalEntryLine>();

}

