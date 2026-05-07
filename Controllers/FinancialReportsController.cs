using System.Security.Claims;
using gas_station.Common;
using gas_station.Data.Context;
using gas_station.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace gas_station.Controllers;

internal sealed record ReportJournalLineRow(
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
    /// <summary>When set with no parent, account is business staging (temporary) — excluded from balance sheet math.</summary>
    int? AccountBusinessId,
    int? AccountParentAccountId);

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FinancialReportsController(GasStationDBContext db) : ControllerBase
{
    private const string SuperAdminRole = "SuperAdmin";
    private static bool IsSuperAdmin(ClaimsPrincipal user) => user.IsInRole(SuperAdminRole);

    private bool TryGetJwtBusiness(out int businessId)
    {
        businessId = 0;
        var bidStr = User.FindFirstValue("business_id");
        return !string.IsNullOrEmpty(bidStr) && int.TryParse(bidStr, out businessId);
    }

    /// <summary>
    /// Station scope for financial reports: only when the client passes <c>stationId</c> &gt; 0.
    /// <see cref="FilterLines"/> then includes that station&apos;s entries <b>plus</b> business-wide journals
    /// (<see cref="JournalEntry.StationId"/> null), e.g. period close. We do <b>not</b> fall back to JWT <c>station_id</c>.
    /// </summary>
    private static int? ResolveStationFilterForReports(int? stationId) =>
        stationId is > 0 ? stationId.Value : null;

    private bool ResolveBusiness(int businessId, out int bid, out IActionResult? err)
    {
        bid = 0;
        err = null;
        if (IsSuperAdmin(User))
        {
            if (businessId <= 0) { err = BadRequest("businessId is required."); return false; }
            bid = businessId; return true;
        }
        if (!TryGetJwtBusiness(out var jwtBid)) { err = BadRequest("No business assigned."); return false; }
        if (businessId > 0 && businessId != jwtBid) { err = Forbid(); return false; }
        bid = jwtBid; return true;
    }

    /// <param name="trialBalanceMode">adjusted (default): exclude closing entries. unadjusted: exclude adjusting and closing. postclosing: include all.</param>
    private IQueryable<ReportJournalLineRow> FilterLines(int bid, DateTime? from, DateTime? to, int? stationId, string? trialBalanceMode = null)
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
        // Business-wide journals (e.g. period close) have null StationId — still include them when scoping by station.
        if (stationId.HasValue) q = q.Where(x => x.e.StationId == null || x.e.StationId == stationId.Value);
        return q.Select(x => new ReportJournalLineRow(
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

    /// <summary>
    /// Maps UI trial-balance view to <see cref="FilterLines"/> mode. <c>adjusted</c> excludes closing; <c>postclosing</c>
    /// includes closing (balance sheet / cumulative equity as after close). Never collapse <c>adjusted</c> into
    /// <c>postclosing</c> — that incorrectly included closing lines in the adjusted balance sheet and distorted P&amp;L.
    /// </summary>
    private static string StatementReportTrialBalanceMode(string? requested)
    {
        return requested?.Trim().ToLowerInvariant() switch
        {
            "unadjusted" => "unadjusted",
            "postclosing" => "postclosing",
            "adjusted" => "adjusted",
            _ => "adjusted",
        };
    }

    /// <summary>
    /// Journal scope for <b>income statement</b> activity (revenue, COGS, expense) and period net income.
    /// Closing entries belong on the balance sheet / equity (clearing nominals to retained earnings), not in period P&amp;L;
    /// including them when their date falls inside a new open period blends the prior period&apos;s close into the new period.
    /// Post-closing P&amp;L therefore uses the same line filter as adjusted (exclude closing).
    /// </summary>
    private static string IncomeStatementTrialBalanceMode(string? requested) =>
        string.Equals(requested?.Trim(), "unadjusted", StringComparison.OrdinalIgnoreCase)
            ? "unadjusted"
            : "adjusted";

    /// <summary>Business-scoped top-level staging account (not linked under chart parent). Omit from balance sheet totals/lines.</summary>
    private static bool IsTemporaryBusinessStagingAccount(int? accountBusinessId, int? accountParentAccountId) =>
        accountBusinessId is > 0 && (accountParentAccountId is null or 0);

    [HttpGet("trial-balance")]
    public IActionResult TrialBalance(
        [FromQuery] int businessId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int? stationId,
        [FromQuery] string? trialBalanceMode = null)
    {
        if (!ResolveBusiness(businessId, out var bid, out var err)) return err!;
        var stationFilter = ResolveStationFilterForReports(stationId);
        var rows = FilterLines(bid, from, to, stationFilter, trialBalanceMode)
            .AsEnumerable()
            .Where(x => !IsTemporaryBusinessStagingAccount(x.AccountBusinessId, x.AccountParentAccountId))
            .GroupBy(x => new { x.AccountId, x.AccountCode, x.AccountName })
            .Select(g => new
            {
                accountId = g.Key.AccountId,
                code = g.Key.AccountCode,
                name = g.Key.AccountName,
                debit = g.Sum(x => x.Debit),
                credit = g.Sum(x => x.Credit),
                balance = g.Sum(x => x.Debit - x.Credit)
            })
            .OrderBy(x => x.code)
            .ToList();
        return Ok(rows);
    }

    /// <summary>
    /// Per-account balances for chart-of-accounts UI: normal balance sign by type (Asset/Expense/COGS = debit−credit;
    /// Liability/Equity/Income/Revenue = credit−debit). Same journal scope as trial balance.
    /// </summary>
    [HttpGet("accounts-with-balances")]
    public IActionResult AccountsWithBalances(
        [FromQuery] int businessId,
        [FromQuery] DateTime? to,
        [FromQuery] int? stationId,
        [FromQuery] string? trialBalanceMode = null)
    {
        if (!ResolveBusiness(businessId, out var bid, out var err)) return err!;
        var stationFilter = ResolveStationFilterForReports(stationId);
        var rows = FilterLines(bid, null, to, stationFilter, trialBalanceMode)
            .AsEnumerable()
            .Where(x => !IsTemporaryBusinessStagingAccount(x.AccountBusinessId, x.AccountParentAccountId))
            .GroupBy(x => new { x.AccountId, x.AccountCode, x.AccountName, x.AccountType })
            .Select(g =>
            {
                var debit = g.Sum(x => x.Debit);
                var credit = g.Sum(x => x.Credit);
                var balance = DisplayBalanceForCoaTree(g.Key.AccountType, debit, credit);
                return new
                {
                    id = g.Key.AccountId,
                    name = g.Key.AccountName,
                    code = g.Key.AccountCode,
                    type = g.Key.AccountType,
                    balance,
                };
            })
            .OrderBy(x => x.code)
            .ToList();
        return Ok(rows);
    }

    /// <summary>Statement-style balance for tree display (positive = normal balance for that type).</summary>
    private static double DisplayBalanceForCoaTree(string? accountType, double debit, double credit)
    {
        if (string.Equals(accountType, "Asset", StringComparison.OrdinalIgnoreCase)
            || string.Equals(accountType, "Expense", StringComparison.OrdinalIgnoreCase)
            || IsCogsType(accountType))
            return debit - credit;
        if (string.Equals(accountType, "Liability", StringComparison.OrdinalIgnoreCase)
            || string.Equals(accountType, "Equity", StringComparison.OrdinalIgnoreCase)
            || IsIncomeType(accountType)
            || string.Equals(accountType, "Revenue", StringComparison.OrdinalIgnoreCase))
            return credit - debit;
        return debit - credit;
    }

    [HttpGet("general-ledger")]
    public IActionResult GeneralLedger(
        [FromQuery] int businessId,
        [FromQuery] int? accountId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int? stationId,
        [FromQuery] string? trialBalanceMode = null)
    {
        if (!ResolveBusiness(businessId, out var bid, out var err)) return err!;
        var stationFilter = ResolveStationFilterForReports(stationId);
        var rows = FilterLines(bid, from, to, stationFilter, trialBalanceMode)
            .AsEnumerable()
            .Where(x => !accountId.HasValue || accountId.Value <= 0 || x.AccountId == accountId.Value)
            .OrderBy(x => x.Date)
            .ThenBy(x => x.EntryId)
            .Select(x => new
            {
                date = x.Date,
                x.Description,
                x.Debit,
                x.Credit,
                x.Remark,
            }).ToList();
        return Ok(rows);
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
        return code.StartsWith("5", StringComparison.OrdinalIgnoreCase);
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
            || n.Contains("rent", StringComparison.OrdinalIgnoreCase)
            || n.Contains("salary", StringComparison.OrdinalIgnoreCase)
            || n.Contains("wage", StringComparison.OrdinalIgnoreCase)
            || n.Contains("utility", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBalanceSheetAccountType(string? t) =>
        string.Equals(t, "Asset", StringComparison.OrdinalIgnoreCase)
        || string.Equals(t, "Liability", StringComparison.OrdinalIgnoreCase)
        || string.Equals(t, "Equity", StringComparison.OrdinalIgnoreCase);

    private sealed record StatementAccountRow(string code, string name, double amount);

    private sealed record ProfitLossReportData(
        List<StatementAccountRow> IncomeAccounts,
        double IncomeTotal,
        List<StatementAccountRow> CogsAccounts,
        double CogsTotal,
        List<StatementAccountRow> ExpenseAccounts,
        double ExpenseTotal,
        double GrossProfit,
        double NetOrdinaryIncome,
        double NetIncome);

    private sealed record BalanceSheetReportData(
        double Assets,
        double Liabilities,
        double Equity,
        double LiabilitiesAndEquity,
        List<object> AssetAccounts,
        List<object> LiabilityAccounts,
        List<object> EquityAccounts);

    private ProfitLossReportData BuildProfitLossReportData(
        int businessId,
        DateTime? from,
        DateTime? to,
        int? stationId,
        string? trialBalanceMode)
    {
        var raw = FilterLines(businessId, from, to, stationId, IncomeStatementTrialBalanceMode(trialBalanceMode))
            .AsEnumerable()
            .Where(x => !IsTemporaryBusinessStagingAccount(x.AccountBusinessId, x.AccountParentAccountId))
            .ToList();

        var byAccount = raw
            .GroupBy(x => new { x.AccountId, x.AccountCode, x.AccountName, x.AccountType })
            .Select(g => new
            {
                g.Key.AccountId,
                g.Key.AccountCode,
                g.Key.AccountName,
                g.Key.AccountType,
                Amount = g.Sum(x => PlSignedAmountForLine(g.Key.AccountType, g.Key.AccountCode, g.Key.AccountName, x.Debit, x.Credit)),
            })
            .Where(x => Math.Abs(x.Amount) > 0.000001)
            .ToList();

        var incomeAccounts = byAccount
            .Where(x => IsIncomeType(x.AccountType) || IsIncomeClassCode(x.AccountCode) || IsIncomeName(x.AccountName))
            .OrderBy(x => x.AccountCode)
            .Select(x => new StatementAccountRow(x.AccountCode, x.AccountName, x.Amount))
            .ToList();
        var cogsAccounts = byAccount
            .Where(x => IsCogsType(x.AccountType) || IsCogsClassCode(x.AccountCode) || IsCogsName(x.AccountName))
            .OrderBy(x => x.AccountCode)
            .Select(x => new StatementAccountRow(x.AccountCode, x.AccountName, x.Amount))
            .ToList();
        var expenseAccounts = byAccount
            .Where(x =>
                IsExpenseType(x.AccountType) ||
                IsExpenseClassCode(x.AccountCode) ||
                IsExpenseName(x.AccountName) ||
                (!IsBalanceSheetAccountType(x.AccountType)
                    && !IsIncomeType(x.AccountType)
                    && !IsIncomeClassCode(x.AccountCode)
                    && !IsIncomeName(x.AccountName)
                    && !IsCogsType(x.AccountType)
                    && !IsCogsClassCode(x.AccountCode)
                    && !IsCogsName(x.AccountName)))
            .OrderBy(x => x.AccountCode)
            .Select(x => new StatementAccountRow(x.AccountCode, x.AccountName, x.Amount))
            .ToList();

        var incomeTotal = byAccount.Where(x => IsIncomeType(x.AccountType) || IsIncomeClassCode(x.AccountCode) || IsIncomeName(x.AccountName)).Sum(x => x.Amount);
        var cogsTotal = byAccount.Where(x => IsCogsType(x.AccountType) || IsCogsClassCode(x.AccountCode) || IsCogsName(x.AccountName)).Sum(x => x.Amount);
        var expenseTotal = byAccount.Where(x =>
            IsExpenseType(x.AccountType) ||
            IsExpenseClassCode(x.AccountCode) ||
            IsExpenseName(x.AccountName) ||
            (!IsBalanceSheetAccountType(x.AccountType)
                && !IsIncomeType(x.AccountType)
                && !IsIncomeClassCode(x.AccountCode)
                && !IsIncomeName(x.AccountName)
                && !IsCogsType(x.AccountType)
                && !IsCogsClassCode(x.AccountCode)
                && !IsCogsName(x.AccountName)))
            .Sum(x => x.Amount);
        var grossProfit = incomeTotal - cogsTotal;
        var netOrdinaryIncome = grossProfit - expenseTotal;

        return new ProfitLossReportData(
            incomeAccounts,
            incomeTotal,
            cogsAccounts,
            cogsTotal,
            expenseAccounts,
            expenseTotal,
            grossProfit,
            netOrdinaryIncome,
            netOrdinaryIncome
        );
    }

    private BalanceSheetReportData BuildBalanceSheetReportData(
        int businessId,
        DateTime? to,
        int? stationId,
        string? trialBalanceMode)
    {
        var allLines = FilterLines(businessId, null, to, stationId, StatementReportTrialBalanceMode(trialBalanceMode)).AsEnumerable().ToList();
        var bsLines = allLines
            .Where(x => !IsTemporaryBusinessStagingAccount(x.AccountBusinessId, x.AccountParentAccountId))
            .ToList();
        var assets = bsLines.Where(x => string.Equals(x.AccountType, "Asset", StringComparison.OrdinalIgnoreCase))
            .Sum(x => x.Debit - x.Credit);
        var liabilities = bsLines.Where(x => string.Equals(x.AccountType, "Liability", StringComparison.OrdinalIgnoreCase))
            .Sum(x => x.Credit - x.Debit);
        var equity = bsLines.Where(x => string.Equals(x.AccountType, "Equity", StringComparison.OrdinalIgnoreCase))
            .Sum(x => x.Credit - x.Debit);

        var bsByAccount = bsLines
            .Where(x => IsBalanceSheetAccountType(x.AccountType))
            .GroupBy(x => new { x.AccountId, x.AccountCode, x.AccountName, x.AccountType })
            .Select(g =>
            {
                var t = g.Key.AccountType ?? "";
                var balance = string.Equals(t, "Asset", StringComparison.OrdinalIgnoreCase)
                    ? g.Sum(x => x.Debit - x.Credit)
                    : g.Sum(x => x.Credit - x.Debit);
                return new { g.Key.AccountId, g.Key.AccountCode, g.Key.AccountName, g.Key.AccountType, Balance = balance };
            })
            .Where(x => Math.Abs(x.Balance) > 0.000001)
            .ToList();

        var assetAccounts = bsByAccount
            .Where(x => string.Equals(x.AccountType, "Asset", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.AccountCode)
            .Select(x => new { code = x.AccountCode, name = x.AccountName, balance = x.Balance })
            .Cast<object>()
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

        return new BalanceSheetReportData(
            assets,
            liabilities,
            equity,
            liabilities + equity,
            assetAccounts,
            liabilityAccounts,
            equityAccounts
        );
    }

    private static double PlSignedAmountForLine(string? accountType, string? accountCode, string? accountName, double debit, double credit)
    {
        if (IsIncomeType(accountType) || IsIncomeClassCode(accountCode) || IsIncomeName(accountName)) return credit - debit;
        if (IsCogsType(accountType) || IsCogsClassCode(accountCode) || IsCogsName(accountName)
            || IsExpenseType(accountType) || IsExpenseClassCode(accountCode) || IsExpenseName(accountName))
            return debit - credit;
        if (!IsBalanceSheetAccountType(accountType))
        {
            // Fallback for custom P&L account typing: use dominant side so period view does not collapse to zero.
            return credit >= debit ? credit - debit : debit - credit;
        }
        return 0;
    }

    [HttpGet("profit-loss")]
    public IActionResult ProfitLoss(
        [FromQuery] int businessId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int? stationId,
        [FromQuery] string? trialBalanceMode = null)
    {
        if (!ResolveBusiness(businessId, out var bid, out var err)) return err!;
        var stationFilter = ResolveStationFilterForReports(stationId);
        var raw = FilterLines(bid, from, to, stationFilter, IncomeStatementTrialBalanceMode(trialBalanceMode))
            .AsEnumerable()
            .Where(x => !IsTemporaryBusinessStagingAccount(x.AccountBusinessId, x.AccountParentAccountId))
            .ToList();

        static double ClassicPlSignedAmountForLine(string? accountType, double debit, double credit)
        {
            if (IsIncomeType(accountType)) return credit - debit;
            if (IsCogsType(accountType) || IsExpenseType(accountType)) return debit - credit;
            return 0;
        }

        var byAccount = raw
            .GroupBy(x => new { x.AccountId, x.AccountCode, x.AccountName, x.AccountType })
            .Select(g => new
            {
                g.Key.AccountId,
                g.Key.AccountCode,
                g.Key.AccountName,
                g.Key.AccountType,
                Amount = g.Sum(x => ClassicPlSignedAmountForLine(g.Key.AccountType, x.Debit, x.Credit)),
            })
            .Where(x => Math.Abs(x.Amount) > 0.000001)
            .ToList();

        var incomeAccounts = byAccount
            .Where(x => IsIncomeType(x.AccountType))
            .OrderBy(x => x.AccountCode)
            .Select(x => new { code = x.AccountCode, name = x.AccountName, amount = x.Amount })
            .ToList();
        var cogsAccounts = byAccount
            .Where(x => IsCogsType(x.AccountType))
            .OrderBy(x => x.AccountCode)
            .Select(x => new { code = x.AccountCode, name = x.AccountName, amount = x.Amount })
            .ToList();
        var expenseAccounts = byAccount
            .Where(x => IsExpenseType(x.AccountType) || IsExpenseClassCode(x.AccountCode))
            .OrderBy(x => x.AccountCode)
            .Select(x => new { code = x.AccountCode, name = x.AccountName, amount = x.Amount })
            .ToList();

        var incomeTotal = incomeAccounts.Sum(x => x.amount);
        var cogsTotal = cogsAccounts.Sum(x => x.amount);
        var expenseTotal = expenseAccounts.Sum(x => x.amount);
        var grossProfit = incomeTotal - cogsTotal;
        var netOrdinaryIncome = grossProfit - expenseTotal;

        return Ok(new
        {
            incomeAccounts,
            incomeTotal,
            cogsAccounts,
            cogsTotal,
            expenseAccounts,
            expenseTotal,
            grossProfit,
            netOrdinaryIncome,
            netIncome = netOrdinaryIncome,
        });
    }

    [HttpGet("balance-sheet")]
    public IActionResult BalanceSheet(
        [FromQuery] int businessId,
        [FromQuery] DateTime? to,
        [FromQuery] int? stationId,
        [FromQuery] string? trialBalanceMode = null)
    {
        if (!ResolveBusiness(businessId, out var bid, out var err)) return err!;
        var stationFilter = ResolveStationFilterForReports(stationId);
        var bs = BuildBalanceSheetReportData(bid, to, stationFilter, trialBalanceMode);
        return Ok(new
        {
            assets = bs.Assets,
            liabilities = bs.Liabilities,
            equity = bs.Equity,
            liabilitiesAndEquity = bs.LiabilitiesAndEquity,
            assetAccounts = bs.AssetAccounts,
            liabilityAccounts = bs.LiabilityAccounts,
            equityAccounts = bs.EquityAccounts,
        });
    }

    [HttpGet("report-period-view")]
    public IActionResult ReportPeriodView(
        [FromQuery] int businessId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int? stationId,
        [FromQuery] string? trialBalanceMode = null)
    {
        if (!ResolveBusiness(businessId, out var bid, out var err)) return err!;
        var stationFilter = ResolveStationFilterForReports(stationId);
        var pl = BuildProfitLossReportData(bid, from, to, stationFilter, trialBalanceMode);
        var bs = BuildBalanceSheetReportData(bid, to, stationFilter, trialBalanceMode);
        var operatingNetCash = pl.IncomeTotal - pl.ExpenseTotal;

        return Ok(new
        {
            incomeStatement = new
            {
                incomeAccounts = pl.IncomeAccounts,
                cogsAccounts = pl.CogsAccounts,
                expenseAccounts = pl.ExpenseAccounts,
                sales = pl.IncomeTotal,
                cogs = pl.CogsTotal,
                grossProfit = pl.GrossProfit,
                totalExpense = pl.ExpenseTotal,
                netIncome = pl.NetIncome,
            },
            balanceSheet = new
            {
                totalAsset = bs.Assets,
                totalEquity = bs.Equity,
                netIncome = pl.NetIncome,
                assets = bs.AssetAccounts,
                liabilities = bs.LiabilityAccounts,
                equity = bs.EquityAccounts,
            },
            cashFlowStatement = new
            {
                receivedAccounts = pl.IncomeAccounts,
                paidAccounts = pl.ExpenseAccounts,
                cashReceivedFromFuelSales = pl.IncomeTotal,
                cashPaidForExpense = pl.ExpenseTotal,
                netCashFromOperating = operatingNetCash,
            },
        });
    }

    /// <summary>
    /// Statement of changes in equity: each equity account with beginning balance (through day before <paramref name="from"/>),
    /// period change, and ending balance (through <paramref name="to"/>). Uses the same journal scope as the balance sheet.
    /// Also returns net income for the period (Income Statement) for reference.
    /// </summary>
    [HttpGet("capital-statement")]
    public IActionResult CapitalStatement(
        [FromQuery] int businessId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int? stationId,
        [FromQuery] string? trialBalanceMode = null)
    {
        if (!ResolveBusiness(businessId, out var bid, out var err)) return err!;
        if (!from.HasValue || !to.HasValue)
            return BadRequest("from and to are required.");
        var fromDate = from.Value.Date;
        var toDate = to.Value.Date;
        if (fromDate > toDate)
            return BadRequest("from must be on or before to.");

        var mode = StatementReportTrialBalanceMode(trialBalanceMode);
        var stationFilter = ResolveStationFilterForReports(stationId);

        static bool IsEquityStaging(ReportJournalLineRow x) =>
            string.Equals(x.AccountType, "Equity", StringComparison.OrdinalIgnoreCase)
            && !IsTemporaryBusinessStagingAccount(x.AccountBusinessId, x.AccountParentAccountId);

        var beginningTo = fromDate.AddDays(-1);
        var begLines = beginningTo >= new DateTime(1900, 1, 1)
            ? FilterLines(bid, null, beginningTo, stationFilter, mode).AsEnumerable().Where(IsEquityStaging).ToList()
            : new List<ReportJournalLineRow>();

        var endLines = FilterLines(bid, null, toDate, stationFilter, mode).AsEnumerable().Where(IsEquityStaging).ToList();

        static Dictionary<int, (string Code, string Name, double Balance)> EquityBalances(IEnumerable<ReportJournalLineRow> lines)
        {
            return lines
                .GroupBy(x => new { x.AccountId, x.AccountCode, x.AccountName })
                .ToDictionary(
                    g => g.Key.AccountId,
                    g => (g.Key.AccountCode, g.Key.AccountName, g.Sum(x => x.Credit - x.Debit)));
        }

        var begByAccount = EquityBalances(begLines);
        var endByAccount = EquityBalances(endLines);
        var accountIds = begByAccount.Keys.Union(endByAccount.Keys)
            .OrderBy(id =>
            {
                endByAccount.TryGetValue(id, out var em);
                begByAccount.TryGetValue(id, out var bm);
                return (em.Code ?? bm.Code) ?? "";
            })
            .ToList();

        var equityRows = new List<object>();
        double totalBeginning = 0, totalChange = 0, totalEnding = 0;
        foreach (var id in accountIds)
        {
            begByAccount.TryGetValue(id, out var begMeta);
            endByAccount.TryGetValue(id, out var endMeta);
            var beginning = begMeta.Balance;
            var ending = endMeta.Balance;
            var change = ending - beginning;
            var code = endMeta.Code ?? begMeta.Code ?? "";
            var name = endMeta.Name ?? begMeta.Name ?? "";
            equityRows.Add(new
            {
                accountId = id,
                code,
                name,
                beginning,
                change,
                ending,
            });
            totalBeginning += beginning;
            totalChange += change;
            totalEnding += ending;
        }

        var plRaw = FilterLines(bid, fromDate, toDate, stationFilter, IncomeStatementTrialBalanceMode(trialBalanceMode))
            .AsEnumerable()
            .Where(x => !IsTemporaryBusinessStagingAccount(x.AccountBusinessId, x.AccountParentAccountId))
            .ToList();
        var byAccount = plRaw
            .GroupBy(x => new { x.AccountId, x.AccountCode, x.AccountName, x.AccountType })
            .Select(g => new
            {
                g.Key.AccountCode,
                g.Key.AccountName,
                g.Key.AccountType,
                Amount = g.Sum(x => PlSignedAmountForLine(g.Key.AccountType, g.Key.AccountCode, g.Key.AccountName, x.Debit, x.Credit)),
            })
            .Where(x => Math.Abs(x.Amount) > 0.000001)
            .ToList();
        var incomeTotal = byAccount.Where(x => IsIncomeType(x.AccountType) || IsIncomeClassCode(x.AccountCode) || IsIncomeName(x.AccountName)).Sum(x => x.Amount);
        var cogsTotal = byAccount.Where(x => IsCogsType(x.AccountType) || IsCogsClassCode(x.AccountCode) || IsCogsName(x.AccountName)).Sum(x => x.Amount);
        var expenseTotal = byAccount.Where(x =>
            IsExpenseType(x.AccountType) ||
            IsExpenseClassCode(x.AccountCode) ||
            IsExpenseName(x.AccountName) ||
            (!IsBalanceSheetAccountType(x.AccountType)
                && !IsIncomeType(x.AccountType)
                && !IsIncomeClassCode(x.AccountCode)
                && !IsIncomeName(x.AccountName)
                && !IsCogsType(x.AccountType)
                && !IsCogsClassCode(x.AccountCode)
                && !IsCogsName(x.AccountName)))
            .Sum(x => x.Amount);
        var netIncome = incomeTotal - cogsTotal - expenseTotal;

        return Ok(new
        {
            equityRows,
            totalBeginning,
            totalChange,
            totalEnding,
            netIncome,
        });
    }

    [HttpGet("customer-balances")]
    public async Task<IActionResult> CustomerBalances(
        [FromQuery] int businessId,
        [FromQuery] int? stationId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int receivableAccountId)
    {
        if (!ResolveBusiness(businessId, out var bid, out var err)) return err!;
        var stationFilter = ResolveStationFilterForReports(stationId);
        if (receivableAccountId <= 0)
            return BadRequest("Receivable account is required.");

        var acc = await db.Accounts.AsNoTracking()
            .Include(a => a.ChartsOfAccounts)
            .FirstOrDefaultAsync(a => !a.IsDeleted && a.Id == receivableAccountId && a.BusinessId == bid);
        if (acc is null)
            return BadRequest("Invalid receivable account for this business.");
        if (!AccountingSubledgerRules.IsAccountsReceivable(acc))
            return BadRequest("Selected account is not an accounts receivable account.");

        var code = acc.Code;

        // Subledger from posted journals only (same pattern as supplier-balances).
        // AR asset: debits increase receivable ("given"), credits reduce it ("paid").
        var q =
            from l in db.JournalEntryLines.AsNoTracking()
            join e in db.JournalEntries.AsNoTracking() on l.JournalEntryId equals e.Id
            where !l.IsDeleted && !e.IsDeleted && e.BusinessId == bid
                  && l.AccountId == receivableAccountId && l.CustomerId != null && l.CustomerId > 0
            select new { l, e };

        if (from.HasValue)
            q = q.Where(x => x.e.Date >= from.Value.Date);
        if (to.HasValue)
        {
            var toExclusive = to.Value.Date.AddDays(1);
            q = q.Where(x => x.e.Date < toExclusive);
        }

        if (stationFilter.HasValue && stationFilter.Value > 0)
            q = q.Where(x => x.e.StationId == stationFilter.Value);

        var aggregates = await q
            .GroupBy(x => x.l.CustomerId!.Value)
            .Select(g => new
            {
                CustomerFuelGivenId = g.Key,
                GivenAmount = g.Sum(x => x.l.Debit),
                PaidAmount = g.Sum(x => x.l.Credit),
                Balance = g.Sum(x => x.l.Debit - x.l.Credit),
            })
            .ToListAsync();

        var cfgIds = aggregates.Select(x => x.CustomerFuelGivenId).ToList();
        var names = await db.CustomerFuelGivens.AsNoTracking()
            .Where(c => !c.IsDeleted && cfgIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name);

        var rows = aggregates
            .Select(x => new
            {
                code,
                customer = names.GetValueOrDefault(x.CustomerFuelGivenId) ?? $"Customer #{x.CustomerFuelGivenId}",
                givenAmount = x.GivenAmount,
                paidAmount = x.PaidAmount,
                balance = x.Balance,
            })
            .OrderByDescending(x => x.balance)
            .ToList();

        return Ok(rows);
    }

    [HttpGet("supplier-balances")]
    public async Task<IActionResult> SupplierBalances(
        [FromQuery] int businessId,
        [FromQuery] int? stationId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int payableAccountId)
    {
        if (!ResolveBusiness(businessId, out var bid, out var err)) return err!;
        var stationFilter = ResolveStationFilterForReports(stationId);
        if (payableAccountId <= 0)
            return BadRequest("Payable account is required.");

        var acc = await db.Accounts.AsNoTracking()
            .Include(a => a.ChartsOfAccounts)
            .FirstOrDefaultAsync(a => !a.IsDeleted && a.Id == payableAccountId && a.BusinessId == bid);
        if (acc is null)
            return BadRequest("Invalid payable account for this business.");
        if (!AccountingSubledgerRules.IsAccountsPayable(acc))
            return BadRequest("Selected account is not an accounts payable account.");

        var code = acc.Code;

        var q =
            from l in db.JournalEntryLines.AsNoTracking()
            join e in db.JournalEntries.AsNoTracking() on l.JournalEntryId equals e.Id
            where !l.IsDeleted && !e.IsDeleted && e.BusinessId == bid
                  && l.AccountId == payableAccountId && l.SupplierId != null && l.SupplierId > 0
            select new { l, e };

        if (from.HasValue)
            q = q.Where(x => x.e.Date >= from.Value.Date);
        if (to.HasValue)
        {
            var toExclusive = to.Value.Date.AddDays(1);
            q = q.Where(x => x.e.Date < toExclusive);
        }

        if (stationFilter.HasValue && stationFilter.Value > 0)
            q = q.Where(x => x.e.StationId == stationFilter.Value);

        var aggregates = await q
            .GroupBy(x => x.l.SupplierId!.Value)
            .Select(g => new
            {
                SupplierId = g.Key,
                GivenAmount = g.Sum(x => x.l.Credit),
                PaidAmount = g.Sum(x => x.l.Debit),
                Balance = g.Sum(x => x.l.Credit - x.l.Debit),
            })
            .ToListAsync();

        var supplierIds = aggregates.Select(x => x.SupplierId).ToList();
        var names = await db.Suppliers.AsNoTracking()
            .Where(s => !s.IsDeleted && supplierIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name);

        var rows = aggregates
            .Select(x => new
            {
                code,
                supplier = names.GetValueOrDefault(x.SupplierId) ?? $"Supplier #{x.SupplierId}",
                givenAmount = x.GivenAmount,
                paidAmount = x.PaidAmount,
                balance = x.Balance,
            })
            .OrderByDescending(x => x.balance)
            .ToList();

        return Ok(rows);
    }
}

