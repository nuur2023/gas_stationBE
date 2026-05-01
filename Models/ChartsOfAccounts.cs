namespace gas_station.Models;

public class ChartsOfAccounts : BaseModel
{
    public string Type { get; set; } = string.Empty; // Asset, Liability, Equity, Income, Expense, COGS
    public ICollection<Account> Accounts { get; set; } = new List<Account>();
}

