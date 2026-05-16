using gas_station.Data.Context;
using gas_station.Models;
using Microsoft.EntityFrameworkCore;

namespace gas_station.Reporting;

internal sealed record DashJournalLineRow(
    int EntryId,
    DateTime Date,
    string Description,
    int? StationId,
    int AccountId,
    string AccountName,
    string AccountCode,
    string AccountType,
    double Debit,
    double Credit,
    string? Remark,
    int? AccountBusinessId,
    int? AccountParentAccountId);

internal sealed record DashDirectCashFlowDetailRow(string LineKey, string AccountCode, string AccountName, double Amount);

internal static partial class AccountingDashboardFinance
{
    internal static IQueryable<DashJournalLineRow> FilterLines(
        GasStationDBContext db,
        int bid,
        DateTime? from,
        DateTime? to,
        int? stationId,
        string? trialBalanceMode = null)
    {
        var q = db.JournalEntryLines
            .Where(l => !l.IsDeleted)
            .Join(db.JournalEntries.Where(e => !e.IsDeleted && e.BusinessId == bid),
                l => l.JournalEntryId, e => e.Id,
                (l, e) => new { l, e })
            .Join(db.Accounts.Where(a => !a.IsDeleted),
                x => x.l.AccountId, a => a.Id,
                (x, a) => new { x.l, x.e, a })
            .Join(db.ChartsOfAccounts.Where(c => !c.IsDeleted),
                x => x.a.ChartsOfAccountsId, c => c.Id,
                (x, c) => new { x.l, x.e, x.a, c });

        var mode = trialBalanceMode?.Trim().ToLowerInvariant();
        if (mode == "unadjusted")
            q = q.Where(x => x.e.EntryKind != JournalEntryKind.Adjusting && x.e.EntryKind != JournalEntryKind.Closing);
        else if (mode != "postclosing")
            q = q.Where(x => x.e.EntryKind != JournalEntryKind.Closing);

        if (from.HasValue)
        {
            var fromInclusive = from.Value.Date;
            q = q.Where(x => x.e.Date >= fromInclusive);
        }

        if (to.HasValue)
        {
            var toExclusive = to.Value.Date.AddDays(1);
            q = q.Where(x => x.e.Date < toExclusive);
        }

        if (stationId.HasValue) q = q.Where(x => x.e.StationId == null || x.e.StationId == stationId.Value);
        return q.Select(x => new DashJournalLineRow(
            x.e.Id,
            x.e.Date,
            x.e.Description,
            x.e.StationId,
            x.a.Id,
            x.a.Name,
            x.a.Code,
            x.c.Type,
            x.l.Debit,
            x.l.Credit,
            x.l.Remark,
            x.a.BusinessId,
            x.a.ParentAccountId));
    }

    internal static string StatementReportTrialBalanceMode(string? requested) =>
        requested?.Trim().ToLowerInvariant() switch
        {
            "unadjusted" => "unadjusted",
            "postclosing" => "postclosing",
            "adjusted" => "adjusted",
            _ => "adjusted",
        };

    internal static string IncomeStatementTrialBalanceMode(string? requested) =>
        requested?.Trim().ToLowerInvariant() switch
        {
            "unadjusted" => "unadjusted",
            "postclosing" => "postclosing",
            _ => "adjusted",
        };

    /// <summary>
    /// Journal lines on accounts whose chart type is Temporary (e.g. clearing / staging under Temporary parent) are omitted from statements.
    /// Matches chart names <c>Temporary</c> or <c>Temporary Account</c> (case-insensitive).
    /// </summary>
    internal static bool IsTemporaryChartAccount(string? chartsOfAccountsType)
    {
        if (string.IsNullOrWhiteSpace(chartsOfAccountsType)) return false;
        var t = chartsOfAccountsType.Trim();
        return string.Equals(t, "Temporary", StringComparison.OrdinalIgnoreCase)
            || string.Equals(t, "Temporary Account", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsIncomeType(string? t)
    {
        if (string.IsNullOrWhiteSpace(t)) return false;
        var normalized = t.Trim();
        return string.Equals(normalized, "Income", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "Revenue", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "Revenues", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("income", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("revenue", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsIncomeClassCode(string? accountCode)
    {
        if (string.IsNullOrWhiteSpace(accountCode)) return false;
        var code = accountCode.Trim();
        return code.StartsWith("4", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCogsType(string? t) =>
        string.Equals(t, "COGS", StringComparison.OrdinalIgnoreCase)
        || string.Equals(t, "Cogs", StringComparison.OrdinalIgnoreCase)
        || string.Equals(t, "Cost of Goods Sold", StringComparison.OrdinalIgnoreCase)
        || (!string.IsNullOrWhiteSpace(t) && (
            t.Contains("cogs", StringComparison.OrdinalIgnoreCase)
            || t.Contains("cost of goods", StringComparison.OrdinalIgnoreCase)
            || t.Contains("cost of sales", StringComparison.OrdinalIgnoreCase)
        ));

    private static bool IsExpenseType(string? t)
    {
        if (string.IsNullOrWhiteSpace(t)) return false;
        var normalized = t.Trim();
        return string.Equals(normalized, "Expense", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "Expenses", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("expense", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExpenseClassCode(string? accountCode)
    {
        if (string.IsNullOrWhiteSpace(accountCode)) return false;
        var code = accountCode.Trim();
        return code.StartsWith("5", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCogsClassCode(string? accountCode)
    {
        if (string.IsNullOrWhiteSpace(accountCode)) return false;
        var code = accountCode.Trim();
        return code.StartsWith("6", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsIncomeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var n = name.Trim();
        return n.Contains("income", StringComparison.OrdinalIgnoreCase)
            || n.Contains("revenue", StringComparison.OrdinalIgnoreCase)
            || n.Contains("sale", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCogsName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var n = name.Trim();
        return n.Contains("cogs", StringComparison.OrdinalIgnoreCase)
            || n.Contains("cost of goods", StringComparison.OrdinalIgnoreCase)
            || n.Contains("cost of sales", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExpenseName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var n = name.Trim();
        return n.Contains("expense", StringComparison.OrdinalIgnoreCase)
            || (n.Contains("rent", StringComparison.OrdinalIgnoreCase) && !n.Contains("prepaid", StringComparison.OrdinalIgnoreCase))
            || n.Contains("salary", StringComparison.OrdinalIgnoreCase)
            || n.Contains("wage", StringComparison.OrdinalIgnoreCase)
            || n.Contains("utility", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBalanceSheetAccountType(string? t) =>
        string.Equals(t, "Asset", StringComparison.OrdinalIgnoreCase)
        || string.Equals(t, "Liability", StringComparison.OrdinalIgnoreCase)
        || string.Equals(t, "Equity", StringComparison.OrdinalIgnoreCase);

    internal sealed record StatementAccountRow(string code, string name, double amount);

    internal sealed record DashProfitLossReportData(
        List<StatementAccountRow> IncomeAccounts,
        double IncomeTotal,
        List<StatementAccountRow> CogsAccounts,
        double CogsTotal,
        List<StatementAccountRow> ExpenseAccounts,
        double ExpenseTotal,
        double GrossProfit,
        double NetOrdinaryIncome,
        double NetIncome);

    internal sealed record DashBalanceSheetAssetRow(int AccountId, int? ParentAccountId, string Code, string Name, double Balance);

    internal sealed record DashBalanceSheetReportData(
        double Assets,
        double Liabilities,
        double Equity,
        double LiabilitiesAndEquity,
        IReadOnlyList<DashBalanceSheetAssetRow> AssetAccounts,
        List<object> LiabilityAccounts,
        List<object> EquityAccounts);

    internal static bool IsIncomeBucket(string? accountType, string? accountCode, string? accountName) =>
        IsIncomeType(accountType)
        || IsIncomeClassCode(accountCode)
        || IsIncomeName(accountName)
        || (!IsCogsType(accountType) && !IsCogsClassCode(accountCode) && !IsCogsName(accountName)
            && !string.IsNullOrWhiteSpace(accountName)
            && (accountName.Contains("Sales", StringComparison.OrdinalIgnoreCase)
                || accountName.Contains("Revenue", StringComparison.OrdinalIgnoreCase)
                || (accountName.Contains("Fuel", StringComparison.OrdinalIgnoreCase)
                    && !accountName.Contains("Expense", StringComparison.OrdinalIgnoreCase)
                    && !accountName.Contains("COGS", StringComparison.OrdinalIgnoreCase)
                    && !accountName.Contains("cost of", StringComparison.OrdinalIgnoreCase))));

    private static bool IsCogsBucket(string? accountType, string? accountCode, string? accountName) =>
        IsCogsType(accountType) || IsCogsClassCode(accountCode) || IsCogsName(accountName);

    internal static bool IsExpenseBucket(string? accountType, string? accountCode, string? accountName, double amount) =>
        IsExpenseType(accountType)
        || IsExpenseClassCode(accountCode)
        || IsExpenseName(accountName)
        || (!IsBalanceSheetAccountType(accountType)
            && !IsIncomeBucket(accountType, accountCode, accountName)
            && !IsCogsBucket(accountType, accountCode, accountName)
            && amount >= 0);

    private static bool IsExpenseClassification(string? accountType, string? accountCode, string? accountName) =>
        IsExpenseType(accountType) || IsExpenseClassCode(accountCode) || IsExpenseName(accountName);

    internal static double PlAccountPeriodAmount(
        string? accountType,
        string? accountCode,
        string? accountName,
        IEnumerable<(double Debit, double Credit)> lines)
    {
        var rows = lines.ToList();
        if (IsExpenseClassification(accountType, accountCode, accountName))
        {
            var signedNet = rows.Sum(x => x.Debit - x.Credit);
            return Math.Abs(signedNet) < 0.000001 ? 0 : Math.Abs(signedNet);
        }

        return rows.Sum(x => PlSignedAmountForLine(accountType, accountCode, accountName, x.Debit, x.Credit));
    }

    internal static double PlIncomeStatementAmountForLine(string? accountType, string? accountCode, string? accountName, double debit, double credit) =>
        PlSignedAmountForLine(accountType, accountCode, accountName, debit, credit);

    internal static double PlSignedAmountForLine(string? accountType, string? accountCode, string? accountName, double debit, double credit)
    {
        if (IsIncomeType(accountType) || IsIncomeClassCode(accountCode) || IsIncomeName(accountName)) return credit - debit;
        if (IsCogsType(accountType) || IsCogsClassCode(accountCode) || IsCogsName(accountName))
            return debit - credit;
        if (!string.IsNullOrWhiteSpace(accountName)
            && (accountName.Contains("Sales", StringComparison.OrdinalIgnoreCase)
                || accountName.Contains("Revenue", StringComparison.OrdinalIgnoreCase)
                || (accountName.Contains("Fuel", StringComparison.OrdinalIgnoreCase)
                    && !accountName.Contains("Expense", StringComparison.OrdinalIgnoreCase)
                    && !accountName.Contains("COGS", StringComparison.OrdinalIgnoreCase)
                    && !accountName.Contains("cost of", StringComparison.OrdinalIgnoreCase))))
            return credit - debit;
        if (IsExpenseType(accountType) || IsExpenseClassCode(accountCode) || IsExpenseName(accountName))
            return debit - credit;
        if (!IsBalanceSheetAccountType(accountType))
            return credit >= debit ? credit - debit : debit - credit;
        return 0;
    }

    internal static DashProfitLossReportData BuildProfitLossReportData(
        GasStationDBContext db,
        int businessId,
        DateTime? from,
        DateTime? to,
        int? stationId,
        string? trialBalanceMode)
    {
        var raw = FilterLines(db, businessId, from, to, stationId, IncomeStatementTrialBalanceMode(trialBalanceMode))
            .AsEnumerable()
            .Where(x => !IsTemporaryChartAccount(x.AccountType))
            .ToList();

        var byAccount = raw
            .GroupBy(x => new { x.AccountId, x.AccountCode, x.AccountName, x.AccountType })
            .Select(g => new
            {
                g.Key.AccountId,
                g.Key.AccountCode,
                g.Key.AccountName,
                g.Key.AccountType,
                Amount = PlAccountPeriodAmount(
                    g.Key.AccountType,
                    g.Key.AccountCode,
                    g.Key.AccountName,
                    g.Select(x => (x.Debit, x.Credit))),
            })
            .Where(x => Math.Abs(x.Amount) > 0.000001)
            .ToList();

        var incomeAccounts = byAccount
            .Where(x => IsIncomeBucket(x.AccountType, x.AccountCode, x.AccountName))
            .OrderBy(x => x.AccountCode)
            .Select(x => new StatementAccountRow(x.AccountCode, x.AccountName, x.Amount))
            .ToList();
        var cogsAccounts = byAccount
            .Where(x => IsCogsType(x.AccountType) || IsCogsClassCode(x.AccountCode) || IsCogsName(x.AccountName))
            .OrderBy(x => x.AccountCode)
            .Select(x => new StatementAccountRow(x.AccountCode, x.AccountName, x.Amount))
            .ToList();
        var expenseAccounts = byAccount
            .Where(x => IsExpenseBucket(x.AccountType, x.AccountCode, x.AccountName, x.Amount))
            .OrderBy(x => x.AccountCode)
            .Select(x => new StatementAccountRow(x.AccountCode, x.AccountName, x.Amount))
            .ToList();

        var incomeTotal = byAccount.Where(x => IsIncomeBucket(x.AccountType, x.AccountCode, x.AccountName)).Sum(x => x.Amount);
        var cogsTotal = byAccount.Where(x => IsCogsType(x.AccountType) || IsCogsClassCode(x.AccountCode) || IsCogsName(x.AccountName)).Sum(x => x.Amount);
        var expenseTotal = byAccount.Where(x => IsExpenseBucket(x.AccountType, x.AccountCode, x.AccountName, x.Amount)).Sum(x => x.Amount);
        var grossProfit = incomeTotal - cogsTotal;
        var netOrdinaryIncome = grossProfit - expenseTotal;

        return new DashProfitLossReportData(
            incomeAccounts,
            incomeTotal,
            cogsAccounts,
            cogsTotal,
            expenseAccounts,
            expenseTotal,
            grossProfit,
            netOrdinaryIncome,
            netOrdinaryIncome);
    }

    internal static DashBalanceSheetReportData BuildBalanceSheetReportData(
        GasStationDBContext db,
        int businessId,
        DateTime? to,
        int? stationId,
        string? trialBalanceMode)
    {
        var allLines = FilterLines(db, businessId, null, to, stationId, StatementReportTrialBalanceMode(trialBalanceMode)).AsEnumerable().ToList();
        var bsLines = allLines
            .Where(x => !IsTemporaryChartAccount(x.AccountType))
            .ToList();
        var assets = bsLines.Where(x => string.Equals(x.AccountType, "Asset", StringComparison.OrdinalIgnoreCase))
            .Sum(x => x.Debit - x.Credit);
        var liabilities = bsLines.Where(x => string.Equals(x.AccountType, "Liability", StringComparison.OrdinalIgnoreCase))
            .Sum(x => x.Credit - x.Debit);
        var equity = bsLines.Where(x => string.Equals(x.AccountType, "Equity", StringComparison.OrdinalIgnoreCase))
            .Sum(x => x.Credit - x.Debit);

        var bsByAccount = bsLines
            .Where(x => IsBalanceSheetAccountType(x.AccountType))
            .GroupBy(x => new { x.AccountId, x.AccountCode, x.AccountName, x.AccountType, x.AccountParentAccountId })
            .Select(g =>
            {
                var t = g.Key.AccountType ?? "";
                var balance = string.Equals(t, "Asset", StringComparison.OrdinalIgnoreCase)
                    ? g.Sum(x => x.Debit - x.Credit)
                    : g.Sum(x => x.Credit - x.Debit);
                return new
                {
                    g.Key.AccountId,
                    g.Key.AccountCode,
                    g.Key.AccountName,
                    g.Key.AccountType,
                    g.Key.AccountParentAccountId,
                    Balance = balance,
                };
            })
            .Where(x => Math.Abs(x.Balance) > 0.000001)
            .ToList();

        var assetAccounts = bsByAccount
            .Where(x => string.Equals(x.AccountType, "Asset", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.AccountCode)
            .Select(x => new DashBalanceSheetAssetRow(
                x.AccountId,
                x.AccountParentAccountId,
                x.AccountCode ?? "",
                x.AccountName ?? "",
                x.Balance))
            .ToList();
        var liabilityAccounts = bsByAccount
            .Where(x => string.Equals(x.AccountType, "Liability", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.AccountCode)
            .Select(x => new { code = x.AccountCode, name = x.AccountName, balance = x.Balance })
            .Cast<object>()
            .ToList();
        var equityAccounts = bsByAccount
            .Where(x => string.Equals(x.AccountType, "Equity", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.AccountCode)
            .Select(x => new { code = x.AccountCode, name = x.AccountName, balance = x.Balance })
            .Cast<object>()
            .ToList();

        return new DashBalanceSheetReportData(
            assets,
            liabilities,
            equity,
            liabilities + equity,
            assetAccounts,
            liabilityAccounts,
            equityAccounts);
    }
}
