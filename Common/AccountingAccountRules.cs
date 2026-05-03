using gas_station.Models;

namespace gas_station.Common;

public static class AccountingAccountRules
{
    public static bool IsDebitSideForRecurring(Account account)
    {
        var t = account.ChartsOfAccounts?.Type?.Trim() ?? "";
        return t.Equals("Asset", StringComparison.OrdinalIgnoreCase)
               || t.Equals("Expense", StringComparison.OrdinalIgnoreCase)
               || t.Equals("COGS", StringComparison.OrdinalIgnoreCase)
               || t.Equals("Cogs", StringComparison.OrdinalIgnoreCase)
               || t.Equals("Cost of Goods Sold", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Credit line: liability, equity, or any asset (e.g. cash/bank).</summary>
    public static bool IsCreditSideForRecurring(Account account)
    {
        var t = account.ChartsOfAccounts?.Type?.Trim() ?? "";
        return t.Equals("Liability", StringComparison.OrdinalIgnoreCase)
               || t.Equals("Equity", StringComparison.OrdinalIgnoreCase)
               || t.Equals("Asset", StringComparison.OrdinalIgnoreCase);
    }
}
