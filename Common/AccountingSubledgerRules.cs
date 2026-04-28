using gas_station.Models;

namespace gas_station.Common;

/// <summary>
/// Detects AR/AP accounts from chart type + name so manual journals can require subledger links.
/// </summary>
public static class AccountingSubledgerRules
{
    public static bool IsAccountsReceivable(Account account)
    {
        var type = account.ChartsOfAccounts?.Type?.Trim() ?? string.Empty;
        if (!string.Equals(type, "Asset", StringComparison.OrdinalIgnoreCase))
            return false;
        var n = account.Name ?? string.Empty;
        return n.Contains("receivable", StringComparison.OrdinalIgnoreCase)
               || n.Contains("a/r", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsAccountsPayable(Account account)
    {
        var type = account.ChartsOfAccounts?.Type?.Trim() ?? string.Empty;
        if (!string.Equals(type, "Liability", StringComparison.OrdinalIgnoreCase))
            return false;
        var n = account.Name ?? string.Empty;
        return n.Contains("payable", StringComparison.OrdinalIgnoreCase);
    }
}
