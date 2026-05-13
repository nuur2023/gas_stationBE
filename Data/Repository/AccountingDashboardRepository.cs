using gas_station.Data.Context;
using gas_station.Data.Interfaces;
using gas_station.Models;
using gas_station.Reporting;
using gas_station.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace gas_station.Data.Repository;

public class AccountingDashboardRepository(GasStationDBContext db) : IAccountingDashboardRepository
{
    private const double LowCashThreshold = 500;
    public async Task<AccountingDashboardOverviewDto> GetOverviewAsync(int businessId, int? stationId, CancellationToken cancellationToken = default)
    {
        var stationFilter = stationId is > 0 ? stationId : null;
        var today = DateTime.Today;
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var prevMonthStart = monthStart.AddMonths(-1);
        var daysIntoMonth = (today - monthStart).Days;
        var prevMonthEnd = prevMonthStart.AddDays(daysIntoMonth);
        var lastDayPrev = monthStart.AddDays(-1);
        if (prevMonthEnd > lastDayPrev) prevMonthEnd = lastDayPrev;

        var trialMode = (string?)null;

        var plThis = AccountingDashboardFinance.BuildProfitLossReportData(db, businessId, monthStart, today, stationFilter, trialMode);
        var plPrev = AccountingDashboardFinance.BuildProfitLossReportData(db, businessId, prevMonthStart, prevMonthEnd, stationFilter, trialMode);

        // Liquidity KPIs: business-wide, no station filter. Use no end-date so journals dated in the future still
        // affect cash/bank totals (users sometimes post transfers with an upcoming date).
        var bsForLiquidKpis = AccountingDashboardFinance.BuildBalanceSheetReportData(db, businessId, to: null, stationId: null, trialMode);
        var accountMap = await db.Accounts.AsNoTracking()
            .Where(a => !a.IsDeleted && (a.BusinessId == null || a.BusinessId == businessId))
            .ToDictionaryAsync(
                a => a.Id,
                a => (Parent: a.ParentAccountId, Code: a.Code ?? "", Name: a.Name ?? ""),
                cancellationToken)
            .ConfigureAwait(false);
        var (cashBal, bankBal, inventoryVal) = SplitCashBankInventory(bsForLiquidKpis, accountMap);

        var expenseBreakdown = BucketExpenses(plThis);

        var cfThis = AccountingDashboardFinance.ComputeCashFlowTotals(db, businessId, monthStart, today, stationFilter, trialMode);

        var cashTrend = new List<AccountingDashboardCashTrendPointDto>();
        for (var k = 5; k >= 0; k--)
        {
            var anchor = new DateTime(today.Year, today.Month, 1).AddMonths(-k);
            var fromM = anchor;
            var toM = anchor.AddMonths(1).AddDays(-1);
            var cf = AccountingDashboardFinance.ComputeCashFlowTotals(db, businessId, fromM, toM, stationFilter, trialMode);
            cashTrend.Add(new AccountingDashboardCashTrendPointDto
            {
                Label = anchor.ToString("MMM yyyy"),
                NetCashChange = cf.NetIncrease,
            });
        }

        var unbalancedSample = await LoadUnbalancedEntrySampleAsync(businessId, stationFilter, cancellationToken).ConfigureAwait(false);

        var alerts = BuildAlerts(
            plThis,
            cfThis,
            cashBal + bankBal,
            inventoryVal,
            unbalancedSample);

        return new AccountingDashboardOverviewDto
        {
            AsOfDate = today,
            BusinessId = businessId,
            StationId = stationFilter,
            Kpis = new AccountingDashboardKpiDto
            {
                TotalRevenue = plThis.IncomeTotal,
                NetProfit = plThis.NetIncome,
                TotalExpenses = plThis.CogsTotal + plThis.ExpenseTotal,
                CashBalance = cashBal,
                BankBalance = bankBal,
                InventoryValue = inventoryVal,
            },
            ProfitLossCompare = new AccountingDashboardPlCompareDto
            {
                ThisMonth = new AccountingDashboardPlBarDto
                {
                    Label = $"{monthStart:MMM d} – {today:MMM d, yyyy}",
                    Revenue = plThis.IncomeTotal,
                    Expenses = plThis.CogsTotal + plThis.ExpenseTotal,
                    Profit = plThis.NetIncome,
                },
                PreviousMonth = new AccountingDashboardPlBarDto
                {
                    Label = $"{prevMonthStart:MMM d} – {prevMonthEnd:MMM d, yyyy}",
                    Revenue = plPrev.IncomeTotal,
                    Expenses = plPrev.CogsTotal + plPrev.ExpenseTotal,
                    Profit = plPrev.NetIncome,
                },
            },
            CashFlowThisMonth = new AccountingDashboardCashFlowDto
            {
                OperatingCashFlow = cfThis.Operating,
                InvestingCashFlow = cfThis.Investing,
                FinancingCashFlow = cfThis.Financing,
                NetCashChange = cfThis.NetIncrease,
            },
            CashTrend = cashTrend,
            ExpenseBreakdownThisMonth = expenseBreakdown,
            RecentTransactions = Array.Empty<AccountingDashboardRecentLineDto>(),
            Alerts = alerts,
        };
    }

    public async Task<AccountingDashboardRecentTransactionsPagedDto> GetRecentTransactionsPagedAsync(
        int businessId,
        int? stationId,
        DateTime fromDate,
        DateTime toDate,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var stationFilter = stationId is > 0 ? stationId : null;
        var fromInclusive = fromDate.Date;
        var toExclusive = toDate.Date.AddDays(1);
        if (fromInclusive > toDate.Date)
            return new AccountingDashboardRecentTransactionsPagedDto { Page = page, PageSize = pageSize };

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var baseQuery =
            from l in db.JournalEntryLines.AsNoTracking()
            join e in db.JournalEntries.AsNoTracking() on l.JournalEntryId equals e.Id
            join a in db.Accounts.AsNoTracking() on l.AccountId equals a.Id
            join c in db.ChartsOfAccounts.AsNoTracking() on a.ChartsOfAccountsId equals c.Id
            where !l.IsDeleted && !e.IsDeleted && !a.IsDeleted && !c.IsDeleted
                  && e.BusinessId == businessId
                  && e.Date >= fromInclusive && e.Date < toExclusive
                  && (!stationFilter.HasValue || e.StationId == null || e.StationId == stationFilter.Value)
            select new { JournalEntryId = e.Id, LineId = l.Id, e.Date, e.Description, a.Name, a.Code, c.Type, l.Debit, l.Credit };

        var total = await baseQuery.CountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await baseQuery
            .OrderByDescending(x => x.Date)
            .ThenByDescending(x => x.JournalEntryId)
            .ThenByDescending(x => x.LineId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var items = rows
            .Select(x => MapJournalLineToRecentDto(x.JournalEntryId, x.Date, x.Description, x.Name, x.Code, x.Type, x.Debit, x.Credit))
            .ToList();

        return new AccountingDashboardRecentTransactionsPagedDto
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize,
        };
    }

    private static AccountingDashboardRecentLineDto MapJournalLineToRecentDto(
        int journalEntryId,
        DateTime date,
        string? description,
        string? accountName,
        string? accountCode,
        string? accountType,
        double debit,
        double credit)
    {
        var t = accountType ?? "";
        var name = accountName ?? "";
        var code = accountCode ?? "";
        var signed = AccountingDashboardFinance.PlSignedAmountForLine(t, code, name, debit, credit);
        var kind = "Journal";
        if (AccountingDashboardFinance.IsIncomeBucket(t, code, name))
            kind = "Sale";
        else if (AccountingDashboardFinance.IsExpenseBucket(t, code, name, signed))
            kind = "Expense";

        var amt = debit > 0.0001 ? debit : -credit;
        return new AccountingDashboardRecentLineDto
        {
            JournalEntryId = journalEntryId,
            Kind = kind,
            Date = date,
            Account = name,
            AccountCode = code,
            Amount = amt,
            Description = description,
        };
    }

    private static AccountingDashboardExpenseBreakdownDto BucketExpenses(AccountingDashboardFinance.DashProfitLossReportData pl)
    {
        double salaries = 0, rent = 0, utilities = 0, supplies = 0, other = 0;
        foreach (var e in pl.ExpenseAccounts)
        {
            var n = (e.name ?? "").Trim();
            var amt = e.amount;
            if (string.IsNullOrEmpty(n) && string.IsNullOrWhiteSpace(e.code))
                continue;

            if (ExpenseNameLooksLikeSalary(n))
                salaries += amt;
            else if (ExpenseNameLooksLikeRent(n))
                rent += amt;
            else if (ExpenseNameLooksLikeUtilities(n))
                utilities += amt;
            else if (ExpenseNameLooksLikeSupplies(n))
                supplies += amt;
            else
                other += amt;
        }

        return new AccountingDashboardExpenseBreakdownDto
        {
            Salaries = salaries,
            Rent = rent,
            Utilities = utilities,
            Supplies = supplies,
            Other = other,
        };
    }

    private static bool ExpenseNameLooksLikeSalary(string name)
    {
        var n = name;
        return n.Contains("salary", StringComparison.OrdinalIgnoreCase)
            || n.Contains("wage", StringComparison.OrdinalIgnoreCase)
            || n.Contains("payroll", StringComparison.OrdinalIgnoreCase)
            || n.Contains("staff cost", StringComparison.OrdinalIgnoreCase)
            || (n.Contains("employee", StringComparison.OrdinalIgnoreCase)
                && (n.Contains("benefit", StringComparison.OrdinalIgnoreCase)
                    || n.Contains("compensation", StringComparison.OrdinalIgnoreCase)));
    }

    private static bool ExpenseNameLooksLikeRent(string name) =>
        name.Contains("rent", StringComparison.OrdinalIgnoreCase)
        && !name.Contains("prepaid", StringComparison.OrdinalIgnoreCase);

    private static bool ExpenseNameLooksLikeUtilities(string name)
    {
        var n = name;
        return n.Contains("utility", StringComparison.OrdinalIgnoreCase)
            || n.Contains("utilities", StringComparison.OrdinalIgnoreCase)
            || n.Contains("electric", StringComparison.OrdinalIgnoreCase)
            || n.Contains("power", StringComparison.OrdinalIgnoreCase)
            || n.Contains("water", StringComparison.OrdinalIgnoreCase)
            || n.Contains("sewer", StringComparison.OrdinalIgnoreCase)
            || n.Contains("internet", StringComparison.OrdinalIgnoreCase)
            || n.Contains("broadband", StringComparison.OrdinalIgnoreCase)
            || n.Contains("telecom", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ExpenseNameLooksLikeSupplies(string name)
    {
        var n = name;
        if (n.Contains("office supply", StringComparison.OrdinalIgnoreCase)) return false;
        return n.Contains("stationery", StringComparison.OrdinalIgnoreCase)
            || n.Contains("stationary", StringComparison.OrdinalIgnoreCase)
            || n.Contains("supplies", StringComparison.OrdinalIgnoreCase)
            || n.Contains("consumable", StringComparison.OrdinalIgnoreCase)
            || (n.Contains("suppl", StringComparison.OrdinalIgnoreCase) && !n.Contains("office supply", StringComparison.OrdinalIgnoreCase));
    }

    private static (double Cash, double Bank, double Inventory) SplitCashBankInventory(
        AccountingDashboardFinance.DashBalanceSheetReportData bs,
        IReadOnlyDictionary<int, (int? Parent, string Code, string Name)> accountMap)
    {
        double cash = 0, bank = 0, inv = 0;
        foreach (var row in bs.AssetAccounts)
        {
            var n = row.Name;
            if (n.Contains("inventory", StringComparison.OrdinalIgnoreCase)
                || n.Contains("laptop", StringComparison.OrdinalIgnoreCase)
                || (n.Contains("fuel", StringComparison.OrdinalIgnoreCase) && n.Contains("stock", StringComparison.OrdinalIgnoreCase)))
            {
                inv += row.Balance;
                continue;
            }

            var tree = row.AccountId > 0
                ? ClassifyAssetUnderCashBankParent(row.AccountId, accountMap)
                : CashBankTreeKind.None;

            if (tree == CashBankTreeKind.UnderCashParent)
                cash += row.Balance;
            else if (tree == CashBankTreeKind.UnderBankParent)
                bank += row.Balance;
            else if (row.AccountId == 0)
                LegacyCashBankSplitByNameAndCode(n, row.Code, row.Balance, ref cash, ref bank);
        }

        return (cash, bank, inv);
    }

    private enum CashBankTreeKind { None, UnderCashParent, UnderBankParent }

    /// <summary>
    /// Cash vs bank KPIs: walk to the chart <b>root</b> (top-level parent) and classify by <b>root account name</b> only
    /// (e.g. Cash Accounts / Bank Accounts). The root row itself is excluded when it matches a liquidity group so header
    /// postings do not double-count. No parent account codes — codes can be anything per business.
    /// </summary>
    private static CashBankTreeKind ClassifyAssetUnderCashBankParent(
        int accountId,
        IReadOnlyDictionary<int, (int? Parent, string Code, string Name)> map)
    {
        if (!TryResolveCoaRoot(accountId, map, out var rootId, out _, out var rootName))
            return CashBankTreeKind.None;

        var bucket = ClassifyLiquidityRootByName(rootName);
        if (accountId == rootId && bucket != CashBankTreeKind.None)
            return CashBankTreeKind.None;

        return bucket;
    }

    /// <summary>Bank root must be detected before cash so names like "Cash at Bank" follow bank rules first.</summary>
    private static CashBankTreeKind ClassifyLiquidityRootByName(string rootName)
    {
        if (RootLooksLikeBankAccountsParent(rootName))
            return CashBankTreeKind.UnderBankParent;
        if (RootLooksLikeCashAccountsParent(rootName))
            return CashBankTreeKind.UnderCashParent;
        return CashBankTreeKind.None;
    }

    private static bool TryResolveCoaRoot(
        int accountId,
        IReadOnlyDictionary<int, (int? Parent, string Code, string Name)> map,
        out int rootId,
        out string rootCode,
        out string rootName)
    {
        rootId = 0;
        rootCode = "";
        rootName = "";
        if (!map.ContainsKey(accountId))
            return false;

        var visited = new HashSet<int>();
        int? current = accountId;
        while (current != null)
        {
            if (!visited.Add(current.Value))
                return false;
            if (!map.TryGetValue(current.Value, out var node))
                return false;
            if (node.Parent == null)
            {
                rootId = current.Value;
                rootCode = node.Code ?? "";
                rootName = node.Name ?? "";
                return true;
            }

            current = node.Parent;
        }

        return false;
    }

    /// <summary>True when <paramref name="name"/> is the top-level <b>Bank Accounts</b>-style group (name-based only).</summary>
    private static bool RootLooksLikeBankAccountsParent(string name)
    {
        var n = name.Trim();
        if (string.Equals(n, "Bank Accounts", StringComparison.OrdinalIgnoreCase)) return true;
        if (n.Contains("bank account", StringComparison.OrdinalIgnoreCase)) return true;
        if (n.Contains("checking", StringComparison.OrdinalIgnoreCase)) return true;
        if (n.Contains("savings", StringComparison.OrdinalIgnoreCase)) return true;
        if (n.Contains("current account", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static bool RootLooksLikeCashAccountsParent(string name)
    {
        if (RootLooksLikeBankAccountsParent(name)) return false;
        var n = name.Trim();
        if (string.Equals(n, "Cash Accounts", StringComparison.OrdinalIgnoreCase)) return true;
        if (n.Contains("cash account", StringComparison.OrdinalIgnoreCase)) return true;
        if (n.Contains("cash", StringComparison.OrdinalIgnoreCase)) return true;
        if (n.Contains("petty", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static void LegacyCashBankSplitByNameAndCode(string name, string code, double balance, ref double cash, ref double bank)
    {
        if (!AccountingDashboardFinance.IsLikelyCashOrBankAsset(name))
            return;
        if (name.Contains("bank", StringComparison.OrdinalIgnoreCase)
            || name.Contains("checking", StringComparison.OrdinalIgnoreCase)
            || name.Contains("savings", StringComparison.OrdinalIgnoreCase))
            bank += balance;
        else
            cash += balance;
    }

    private static List<AccountingDashboardAlertDto> BuildAlerts(
        AccountingDashboardFinance.DashProfitLossReportData plThis,
        AccountingDashboardFinance.CashFlowPeriodTotals cf,
        double cashPlusBank,
        double inventoryVal,
        IReadOnlyList<(int Id, DateTime Date)> unbalanced)
    {
        var list = new List<AccountingDashboardAlertDto>();
        if (cf.NetIncrease < -0.0001)
            list.Add(new AccountingDashboardAlertDto { Code = "negative_cash_flow", Message = "Net cash flow is negative for the current month.", Severity = "warning" });
        if (cashPlusBank < LowCashThreshold)
            list.Add(new AccountingDashboardAlertDto { Code = "low_cash", Message = $"Combined cash and bank balance is below {LowCashThreshold:N0}.", Severity = "warning" });
        if (plThis.NetIncome < -0.0001)
            list.Add(new AccountingDashboardAlertDto { Code = "loss", Message = "The business is showing a net loss month to date.", Severity = "info" });
        if (inventoryVal < -0.0001)
            list.Add(new AccountingDashboardAlertDto { Code = "negative_inventory", Message = "Inventory-related asset balances are negative — review stock and journals.", Severity = "warning" });
        foreach (var u in unbalanced)
            list.Add(new AccountingDashboardAlertDto { Code = "unbalanced_entry", Message = $"Journal entry #{u.Id} ({u.Date:yyyy-MM-dd}) does not balance.", Severity = "error" });
        return list;
    }

    private async Task<IReadOnlyList<(int Id, DateTime Date)>> LoadUnbalancedEntrySampleAsync(
        int businessId,
        int? stationFilter,
        CancellationToken ct)
    {
        var q = db.JournalEntries.AsNoTracking()
            .Where(e => !e.IsDeleted && e.BusinessId == businessId);
        if (stationFilter.HasValue)
            q = q.Where(e => e.StationId == null || e.StationId == stationFilter.Value);

        var rows = await q
            .OrderByDescending(e => e.Date)
            .ThenByDescending(e => e.Id)
            .Take(400)
            .Select(e => new
            {
                e.Id,
                e.Date,
                Dr = e.Lines.Where(l => !l.IsDeleted).Sum(l => (double?)l.Debit) ?? 0,
                Cr = e.Lines.Where(l => !l.IsDeleted).Sum(l => (double?)l.Credit) ?? 0,
            })
            .ToListAsync(ct);

        return rows
            .Where(x => Math.Abs(x.Dr - x.Cr) > 0.01)
            .Take(5)
            .Select(x => (x.Id, x.Date))
            .ToList();
    }

}
