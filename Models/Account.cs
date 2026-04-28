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

    public int? ParentAccountId { get; set; }
    [ValidateNever]
    public Account? ParentAccount { get; set; }
    [ValidateNever]
    public ICollection<Account>? Children { get; set; }

}

