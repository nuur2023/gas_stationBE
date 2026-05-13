using System.Security.Claims;
using gas_station.Common;
using gas_station.Data.Context;
using gas_station.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using gas_station.Reporting;

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
    int? AccountBusinessId,
    int? AccountParentAccountId);

internal sealed record CashFlowLineItem(
    string Description,
    string Code,
    string Name,
    double Amount);

/// <summary>Direct-method cash flow: one row per counterparty account hit by net cash in an entry.</summary>
internal sealed record DirectCashFlowDetailRow(string LineKey, string AccountCode, string AccountName, double Amount);

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
            .Where(x => !AccountingDashboardFinance.IsTemporaryChartAccount(x.AccountType))
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
            .Where(x => !AccountingDashboardFinance.IsTemporaryChartAccount(x.AccountType))
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
        // Expenses use 5xxx in this chart; COGS uses 6xxx (e.g. 6100). Do not overlap with IsExpenseClassCode.
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

    // Income section only — never use "positive P&L amount" as income or expenses/COGS appear under Sales.
    private static bool IsIncomeBucket(string? accountType, string? accountCode, string? accountName) =>
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

    private static bool IsExpenseBucket(string? accountType, string? accountCode, string? accountName, double amount) =>
        IsExpenseType(accountType)
        || IsExpenseClassCode(accountCode)
        || IsExpenseName(accountName)
        || (!IsBalanceSheetAccountType(accountType)
            && !IsIncomeBucket(accountType, accountCode, accountName)
            && !IsCogsBucket(accountType, accountCode, accountName)
            && amount >= 0);

    private static string CashFlowOperatingReceivedDescription(string accountName)
    {
        var n = accountName?.Trim() ?? "";
        if (n.Contains("sale", StringComparison.OrdinalIgnoreCase)
            || n.Contains("revenue", StringComparison.OrdinalIgnoreCase)
            || n.Contains("income", StringComparison.OrdinalIgnoreCase))
            return "Cash received from customers";
        return $"Cash received: {n}";
    }

    private static string CashFlowOperatingPaidDescription(string accountName)
    {
        var n = accountName?.Trim() ?? "";
        if (n.Contains("prepaid rent", StringComparison.OrdinalIgnoreCase)) return "Cash paid for prepaid rent";
        if (n.Contains("rent", StringComparison.OrdinalIgnoreCase)) return "Cash paid for rent";
        if (n.Contains("salary", StringComparison.OrdinalIgnoreCase) || n.Contains("wage", StringComparison.OrdinalIgnoreCase)) return "Cash paid to employees";
        if (n.Contains("utility", StringComparison.OrdinalIgnoreCase)
            || n.Contains("stationery", StringComparison.OrdinalIgnoreCase)
            || n.Contains("supply", StringComparison.OrdinalIgnoreCase))
            return "Cash paid for expenses (utilities, stationery, supplies)";
        if (n.Contains("supplier", StringComparison.OrdinalIgnoreCase) || n.Contains("purchase", StringComparison.OrdinalIgnoreCase)) return "Cash paid to suppliers";
        return $"Cash paid: {n}";
    }

    private static bool IsLikelyCashOrBankAsset(string? accountName)
    {
        if (string.IsNullOrWhiteSpace(accountName)) return false;
        var n = accountName.Trim();
        return n.Contains("cash", StringComparison.OrdinalIgnoreCase)
            || n.Contains("bank", StringComparison.OrdinalIgnoreCase)
            || n.Contains("petty", StringComparison.OrdinalIgnoreCase);
    }

    private static string CashFlowInvestingDescription(string accountName, double amount)
    {
        var n = accountName?.Trim() ?? "";
        if (n.Contains("inventory", StringComparison.OrdinalIgnoreCase) || n.Contains("laptop", StringComparison.OrdinalIgnoreCase))
            return amount >= 0 ? "Purchase of inventory (laptops)" : "Sale of inventory";
        if (n.Contains("equipment", StringComparison.OrdinalIgnoreCase))
            return amount >= 0 ? "Purchase of office equipment" : "Sale of office equipment";
        if (n.Contains("office supply", StringComparison.OrdinalIgnoreCase) || n.Contains("suppl", StringComparison.OrdinalIgnoreCase))
            return amount >= 0 ? "Purchase of office supplies" : "Sale of office supplies";
        return amount >= 0
            ? $"Purchase of {n}"
            : $"Sale of {n}";
    }

    private static string CashFlowFinancingDescription(string? accountType, string accountName, double amount)
    {
        var n = accountName?.Trim() ?? "";
        var isEquity = string.Equals(accountType, "Equity", StringComparison.OrdinalIgnoreCase);
        if (isEquity)
            return amount >= 0 ? "Owner investment" : "Owner withdrawal";
        if (n.Contains("loan", StringComparison.OrdinalIgnoreCase) || n.Contains("payable", StringComparison.OrdinalIgnoreCase))
            return amount >= 0 ? "Loans received" : "Loan repayments";
        return amount >= 0 ? $"Financing inflow: {n}" : $"Financing outflow: {n}";
    }

    private static bool IsPotentialInternalCashTransferLine(ReportJournalLineRow x) =>
        string.Equals(x.AccountType, "Asset", StringComparison.OrdinalIgnoreCase)
        && IsLikelyCashOrBankAsset(x.AccountName);

    private static List<CashFlowLineItem> ConsolidateCashFlowLines(IEnumerable<CashFlowLineItem> rows)
    {
        return rows
            .GroupBy(x => x.Description)
            .Select(g => new CashFlowLineItem(
                g.Key,
                g.First().Code,
                g.First().Name,
                g.Sum(x => x.Amount)))
            .Where(x => Math.Abs(x.Amount) > 0.000001)
            .OrderBy(x => x.Description)
            .ToList();
    }

    private static bool IsCashOrBankAccount(ReportJournalLineRow x) =>
        string.Equals(x.AccountType, "Asset", StringComparison.OrdinalIgnoreCase)
        && IsLikelyCashOrBankAsset(x.AccountName);

    private static bool IsInternalCashTransferEntry(IReadOnlyList<ReportJournalLineRow> lines)
    {
        if (lines.Count < 2) return false;
        return lines.All(IsCashOrBankAccount);
    }

    private static bool NameLooksLikeAccountsReceivable(string? accountType, string? accountName)
    {
        if (!string.Equals(accountType, "Asset", StringComparison.OrdinalIgnoreCase)) return false;
        var n = accountName ?? "";
        return n.Contains("receivable", StringComparison.OrdinalIgnoreCase)
            || n.Contains("a/r", StringComparison.OrdinalIgnoreCase)
            || n.Contains("accounts receivable", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Inventory / fuel stock — used so cash sale entries are not split into bogus operating inflows.</summary>
    private static bool NameLooksLikeInventoryAccount(ReportJournalLineRow x)
    {
        var n = x.AccountName ?? "";
        return n.Contains("inventory", StringComparison.OrdinalIgnoreCase)
            || n.Contains("laptop", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// "Utilities Expense" does not contain the substring "utility" (utilities vs utility) — match both.
    /// </summary>
    private static bool NameLooksLikeUtilitiesExpense(string? accountName)
    {
        var n = accountName ?? "";
        return n.Contains("utility", StringComparison.OrdinalIgnoreCase)
            || n.Contains("utilities", StringComparison.OrdinalIgnoreCase);
    }

    private static bool NameLooksLikeRetainedOrClosingEquity(string? accountName)
    {
        var n = accountName ?? "";
        return n.Contains("retained", StringComparison.OrdinalIgnoreCase)
            || n.Contains("income summary", StringComparison.OrdinalIgnoreCase)
            || n.Contains("closing", StringComparison.OrdinalIgnoreCase)
            || (n.Contains("earnings", StringComparison.OrdinalIgnoreCase) && !n.Contains("owner", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Owner capital / contribution equity (exclude retained earnings and similar).</summary>
    private static bool NameLooksLikeOwnerContributionEquity(string? accountName)
    {
        if (string.IsNullOrWhiteSpace(accountName)) return false;
        if (NameLooksLikeRetainedOrClosingEquity(accountName)) return false;
        var n = accountName;
        return n.Contains("capital", StringComparison.OrdinalIgnoreCase)
            || n.Contains("contributed", StringComparison.OrdinalIgnoreCase)
            || n.Contains("owner investment", StringComparison.OrdinalIgnoreCase)
            || (n.Contains("owner", StringComparison.OrdinalIgnoreCase) && n.Contains("equity", StringComparison.OrdinalIgnoreCase));
    }

    private static string? ClassifyCashInflowLineKey(ReportJournalLineRow c)
    {
        var t = c.AccountType ?? "";
        var n = c.AccountName ?? "";
        var code = c.AccountCode?.Trim() ?? "";

        if (string.Equals(t, "Liability", StringComparison.OrdinalIgnoreCase))
        {
            if (n.Contains("loan", StringComparison.OrdinalIgnoreCase) || n.Contains("note payable", StringComparison.OrdinalIgnoreCase))
                return "loansReceived";
            return "financingOther";
        }

        if (string.Equals(t, "Equity", StringComparison.OrdinalIgnoreCase))
        {
            if (NameLooksLikeRetainedOrClosingEquity(n)) return null;
            if (NameLooksLikeOwnerContributionEquity(n)) return "ownerInvestment";
            return "financingOther";
        }

        if (IsIncomeType(t) || IsIncomeName(n) || code.StartsWith("4", StringComparison.OrdinalIgnoreCase)
            || string.Equals(t, "Revenue", StringComparison.OrdinalIgnoreCase))
            return "sales";

        if (NameLooksLikeAccountsReceivable(t, n)) return "sales";

        return "operatingOtherInflow";
    }

    private static string ClassifyCashOutflowLineKey(ReportJournalLineRow d)
    {
        var t = d.AccountType ?? "";
        var n = d.AccountName ?? "";
        var code = (d.AccountCode ?? "").Trim();

        if (string.Equals(t, "Asset", StringComparison.OrdinalIgnoreCase)
            && n.Contains("prepaid", StringComparison.OrdinalIgnoreCase)
            && n.Contains("rent", StringComparison.OrdinalIgnoreCase))
            return "prepaidRent";

        if (string.Equals(t, "Asset", StringComparison.OrdinalIgnoreCase) && n.Contains("prepaid", StringComparison.OrdinalIgnoreCase))
            return "prepaidRent";

        if (n.Contains("inventory", StringComparison.OrdinalIgnoreCase) || n.Contains("laptop", StringComparison.OrdinalIgnoreCase))
            return "operatingInventory";
        if (n.Contains("equipment", StringComparison.OrdinalIgnoreCase)) return "equipment";

        // Office supplies (balance sheet asset), not supplies expense or inventory
        if (n.Contains("office supply", StringComparison.OrdinalIgnoreCase)
            || (string.Equals(t, "Asset", StringComparison.OrdinalIgnoreCase)
                && n.Contains("suppl", StringComparison.OrdinalIgnoreCase)
                && !n.Contains("inventory", StringComparison.OrdinalIgnoreCase)
                && !n.Contains("prepaid", StringComparison.OrdinalIgnoreCase)))
            return "operatingOfficeSupplies";

        if (n.Contains("salary", StringComparison.OrdinalIgnoreCase) || n.Contains("wage", StringComparison.OrdinalIgnoreCase))
            return "salaries";

        // Typical chart: 54xx utilities, 55xx stationery, 56xx supplies expense (before generic name checks)
        if (code.Length >= 2 && code.StartsWith("54", StringComparison.OrdinalIgnoreCase))
            return "operatingUtilities";
        if (code.Length >= 2 && code.StartsWith("55", StringComparison.OrdinalIgnoreCase))
            return "operatingStationery";
        if (code.Length >= 2 && code.StartsWith("56", StringComparison.OrdinalIgnoreCase))
            return "operatingSuppliesExpense";

        if (NameLooksLikeUtilitiesExpense(n))
            return "operatingUtilities";

        if (n.Contains("stationery", StringComparison.OrdinalIgnoreCase)
            || n.Contains("stationary", StringComparison.OrdinalIgnoreCase))
            return "operatingStationery";

        if ((IsExpenseType(t) || string.Equals(t, "Expense", StringComparison.OrdinalIgnoreCase))
            && n.Contains("suppl", StringComparison.OrdinalIgnoreCase)
            && !n.Contains("office supply", StringComparison.OrdinalIgnoreCase))
            return "operatingSuppliesExpense";

        if (IsCogsType(t) || string.Equals(t, "COGS", StringComparison.OrdinalIgnoreCase))
            return "operatingOtherOutflow";

        if (IsExpenseType(t) || string.Equals(t, "Expense", StringComparison.OrdinalIgnoreCase))
            return "operatingOtherOutflow";

        return "operatingOtherOutflow";
    }

    private static bool IsRentExpensePlRow(StatementAccountRow exp)
    {
        var n = exp.name ?? "";
        return n.Contains("rent", StringComparison.OrdinalIgnoreCase)
            && !n.Contains("prepaid", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Cash payment to vendor liability — debit is to A/P, not expense; split using P&amp;L weights.</summary>
    private static bool IsAccountsPayableDebitLine(ReportJournalLineRow d)
    {
        if (!string.Equals(d.AccountType, "Liability", StringComparison.OrdinalIgnoreCase)) return false;
        var n = d.AccountName ?? "";
        return n.Contains("payable", StringComparison.OrdinalIgnoreCase)
            || n.Contains("a/p", StringComparison.OrdinalIgnoreCase)
            || n.Contains("creditor", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Maps period P&amp;L expense rows to cash-flow keys (rent expense excluded — prepaid rent line used).</summary>
    private static string? CashFlowLineKeyForPlExpenseRow(StatementAccountRow exp)
    {
        if (IsRentExpensePlRow(exp)) return null;
        var code = (exp.code ?? "").Trim();
        var n = exp.name ?? "";
        if (code.StartsWith("52", StringComparison.OrdinalIgnoreCase)
            || n.Contains("salary", StringComparison.OrdinalIgnoreCase)
            || n.Contains("wage", StringComparison.OrdinalIgnoreCase))
            return "salaries";
        if (code.StartsWith("54", StringComparison.OrdinalIgnoreCase) || NameLooksLikeUtilitiesExpense(n))
            return "operatingUtilities";
        if (code.StartsWith("55", StringComparison.OrdinalIgnoreCase)
            || n.Contains("stationery", StringComparison.OrdinalIgnoreCase)
            || n.Contains("stationary", StringComparison.OrdinalIgnoreCase))
            return "operatingStationery";
        if (code.StartsWith("56", StringComparison.OrdinalIgnoreCase)
            || (n.Contains("suppl", StringComparison.OrdinalIgnoreCase) && !n.Contains("office supply", StringComparison.OrdinalIgnoreCase)))
            return "operatingSuppliesExpense";
        if (code.StartsWith("51", StringComparison.OrdinalIgnoreCase))
            return null;
        return "operatingOtherOutflow";
    }

    private sealed record PlExpenseWeight(string LineKey, double Weight, string Code, string Name);

    private static List<PlExpenseWeight> BuildPlOperatingExpenseWeights(ProfitLossReportData pl)
    {
        var list = new List<PlExpenseWeight>();
        foreach (var exp in pl.ExpenseAccounts)
        {
            var key = CashFlowLineKeyForPlExpenseRow(exp);
            if (key is null || exp.amount <= 0.000001) continue;
            list.Add(new PlExpenseWeight(key, exp.amount, (exp.code ?? "").Trim(), exp.name ?? ""));
        }
        return list;
    }

    private static bool IsPayableLikeAccountName(string? accountName)
    {
        var n = accountName ?? "";
        return n.Contains("payable", StringComparison.OrdinalIgnoreCase)
            || n.Contains("a/p", StringComparison.OrdinalIgnoreCase)
            || n.Contains("creditor", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// P&amp;L expense that is explained entirely by journal lines in entries with <b>no</b> net cash movement
    /// (e.g. DR Supplies Expense / CR Office Supply) must not receive a synthetic cash outflow — cash was already
    /// captured when the asset was purchased or is still non-cash accrual.
    /// </summary>
    private static double ExpensePlAmountFromZeroNetCashJournalEntries(
        IReadOnlyList<ReportJournalLineRow> periodLines,
        string expenseAccountCodeTrimmed)
    {
        if (string.IsNullOrEmpty(expenseAccountCodeTrimmed)) return 0;
        var sum = 0.0;
        foreach (var g in periodLines.GroupBy(x => x.EntryId))
        {
            var lines = g.ToList();
            if (lines.Count == 0) continue;
            var netCash = lines.Where(IsCashOrBankAccount).Sum(x => x.Debit - x.Credit);
            if (Math.Abs(netCash) > 0.000001) continue;
            foreach (var line in lines)
            {
                if (!string.Equals((line.AccountCode ?? "").Trim(), expenseAccountCodeTrimmed, StringComparison.OrdinalIgnoreCase))
                    continue;
                var amt = PlSignedAmountForLine(line.AccountType, line.AccountCode, line.AccountName, line.Debit, line.Credit);
                if (!IsExpenseBucket(line.AccountType, line.AccountCode, line.AccountName, amt)) continue;
                sum += amt;
            }
        }
        return sum;
    }

    /// <summary>
    /// When journals never hit the expense account (e.g. DR A/P / CR Cash), prior logic omits stationery &amp; supplies;
    /// top up from P&amp;L amounts only for rows with no per-account cash yet, then reduce A/P outflows so cash is not double-counted.
    /// </summary>
    private static List<DirectCashFlowDetailRow> ApplyPlOperatingExpenseCashShortfallTopUp(
        IReadOnlyList<ReportJournalLineRow> periodLines,
        ProfitLossReportData pl,
        IReadOnlyList<DirectCashFlowDetailRow> rows)
    {
        var result = rows.ToList();
        var additions = new List<DirectCashFlowDetailRow>();
        foreach (var exp in pl.ExpenseAccounts)
        {
            var key = CashFlowLineKeyForPlExpenseRow(exp);
            if (key is null) continue;
            var code = (exp.code ?? "").Trim();
            if (string.IsNullOrEmpty(code)) continue;
            var nonCashJournalPortion = Math.Min(
                exp.amount,
                ExpensePlAmountFromZeroNetCashJournalEntries(periodLines, code));
            var expensePortionExpectingCash = Math.Max(0, exp.amount - nonCashJournalPortion);
            var desired = -expensePortionExpectingCash;
            var actual = result.Where(r => string.Equals(r.AccountCode, code, StringComparison.OrdinalIgnoreCase)).Sum(r => r.Amount);
            if (Math.Abs(actual) > 0.000001) continue;
            if (Math.Abs(desired) < 0.000001) continue;
            var row = new DirectCashFlowDetailRow(key, code, exp.name ?? "", desired);
            additions.Add(row);
            result.Add(row);
        }
        if (additions.Count == 0) return result;

        var recover = -additions.Sum(x => x.Amount);
        if (recover <= 0.000001) return result;

        for (var i = 0; i < result.Count && recover > 0.000001; i++)
        {
            var r = result[i];
            if (!string.Equals(r.LineKey, "operatingOtherOutflow", StringComparison.OrdinalIgnoreCase)) continue;
            if (!IsPayableLikeAccountName(r.AccountName)) continue;
            if (r.Amount >= -0.000001) continue;
            var take = Math.Min(recover, -r.Amount);
            result[i] = r with { Amount = r.Amount + take };
            recover -= take;
        }

        return result;
    }

    /// <summary>
    /// Direct-method: net cash movement per journal entry is allocated to non-cash lines.
    /// Cash sale entries (DR cash / CR revenue + CR inventory) allocate <b>all</b> inflow to revenue lines so
    /// inventory is not mislabeled as operating inflow; inventory-only credits use the <c>operatingInventory</c> line key.
    /// </summary>
    private static List<DirectCashFlowDetailRow> BuildDirectCashFlowDetailRows(
        IReadOnlyList<ReportJournalLineRow> periodLines,
        HashSet<int> internalTransferEntryIds,
        ProfitLossReportData pl)
    {
        var outRows = new List<DirectCashFlowDetailRow>();
        foreach (var g in periodLines.GroupBy(x => x.EntryId))
        {
            if (internalTransferEntryIds.Contains(g.Key)) continue;
            var lines = g.ToList();
            if (IsInternalCashTransferEntry(lines)) continue;

            var netCash = lines.Where(IsCashOrBankAccount).Sum(x => x.Debit - x.Credit);
            if (Math.Abs(netCash) < 0.000001) continue;

            var nonCash = lines.Where(x => !IsCashOrBankAccount(x)).ToList();
            if (nonCash.Count == 0) continue;

            if (netCash > 0)
            {
                var credits = nonCash.Where(x => x.Credit > 0.000001).ToList();
                if (credits.Count == 0) continue;

                var salesCredits = credits.Where(c => ClassifyCashInflowLineKey(c) == "sales").ToList();
                if (salesCredits.Count > 0)
                {
                    var w = salesCredits.Sum(x => x.Credit);
                    foreach (var c in salesCredits)
                    {
                        var amount = netCash * (c.Credit / w);
                        outRows.Add(new DirectCashFlowDetailRow(
                            "sales",
                            c.AccountCode ?? "",
                            c.AccountName ?? "",
                            amount));
                    }
                    continue;
                }

                var invCredits = credits.Where(NameLooksLikeInventoryAccount).ToList();
                if (invCredits.Count == credits.Count)
                {
                    var w = invCredits.Sum(x => x.Credit);
                    foreach (var c in invCredits)
                    {
                        var amount = netCash * (c.Credit / w);
                        outRows.Add(new DirectCashFlowDetailRow(
                            "operatingInventory",
                            c.AccountCode ?? "",
                            c.AccountName ?? "",
                            amount));
                    }
                    continue;
                }

                var weight = credits.Sum(x => x.Credit);
                foreach (var c in credits)
                {
                    var amount = netCash * (c.Credit / weight);
                    var key = ClassifyCashInflowLineKey(c);
                    if (key is null) continue;
                    outRows.Add(new DirectCashFlowDetailRow(
                        key,
                        c.AccountCode ?? "",
                        c.AccountName ?? "",
                        amount));
                }
            }
            else
            {
                var debits = nonCash.Where(x => x.Debit > 0.000001).ToList();
                if (debits.Count == 0) continue;

                if (debits.Count == 1 && IsAccountsPayableDebitLine(debits[0]))
                {
                    var targets = BuildPlOperatingExpenseWeights(pl);
                    var sumW = targets.Sum(x => x.Weight);
                    if (sumW > 0.000001)
                    {
                        foreach (var t in targets)
                        {
                            var amount = netCash * (t.Weight / sumW);
                            outRows.Add(new DirectCashFlowDetailRow(
                                t.LineKey,
                                t.Code,
                                t.Name,
                                amount));
                        }
                        continue;
                    }
                }

                var weight = debits.Sum(x => x.Debit);
                foreach (var d in debits)
                {
                    var amount = netCash * (d.Debit / weight);
                    outRows.Add(new DirectCashFlowDetailRow(
                        ClassifyCashOutflowLineKey(d),
                        d.AccountCode ?? "",
                        d.AccountName ?? "",
                        amount));
                }
            }
        }

        return outRows
            .Where(x => Math.Abs(x.Amount) > 0.000001)
            .ToList();
    }

    private static double SumDirectByKeys(IEnumerable<DirectCashFlowDetailRow> rows, params string[] keys)
    {
        var set = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
        return rows.Where(x => set.Contains(x.LineKey)).Sum(x => x.Amount);
    }

    private ProfitLossReportData BuildProfitLossReportData(
        int businessId,
        DateTime? from,
        DateTime? to,
        int? stationId,
        string? trialBalanceMode)
    {
        var raw = FilterLines(businessId, from, to, stationId, IncomeStatementTrialBalanceMode(trialBalanceMode))
            .AsEnumerable()
            .Where(x => !AccountingDashboardFinance.IsTemporaryChartAccount(x.AccountType))
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
            .Where(x => !AccountingDashboardFinance.IsTemporaryChartAccount(x.AccountType))
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
            .Select(x => new
            {
                accountId = x.AccountId,
                parentAccountId = x.AccountParentAccountId,
                code = x.AccountCode,
                name = x.AccountName,
                balance = x.Balance,
            })
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
            equityAccounts);
    }

    private static double PlSignedAmountForLine(string? accountType, string? accountCode, string? accountName, double debit, double credit)
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
            .Where(x => !AccountingDashboardFinance.IsTemporaryChartAccount(x.AccountType))
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
        var statementMode = StatementReportTrialBalanceMode(trialBalanceMode);
        // P&L and cash-flow activity use income-statement journal scope (unadjusted vs adjusted; never post-closing nominals).
        var incomeStatementJournalMode = IncomeStatementTrialBalanceMode(trialBalanceMode);
        var pl = BuildProfitLossReportData(bid, from, to, stationFilter, incomeStatementJournalMode);
        var bs = BuildBalanceSheetReportData(bid, to, stationFilter, trialBalanceMode);
        // Post-closing balance sheet: period net income is already in equity — do not repeat it as a separate plug line.
        var balanceSheetNetIncomePlug = string.Equals(statementMode, "postclosing", StringComparison.OrdinalIgnoreCase)
            ? 0.0
            : pl.NetIncome;

        var periodLinesAll = FilterLines(bid, from, to, stationFilter, incomeStatementJournalMode)
            .AsEnumerable()
            .Where(x => !AccountingDashboardFinance.IsTemporaryChartAccount(x.AccountType))
            .ToList();

        // Exclude pure internal transfers between cash/bank accounts from cash-flow.
        var internalTransferEntryIds = periodLinesAll
            .GroupBy(x => x.EntryId)
            .Where(g =>
            {
                var rows = g.ToList();
                if (rows.Count < 2) return false;
                return rows.All(IsPotentialInternalCashTransferLine);
            })
            .Select(g => g.Key)
            .ToHashSet();

        var periodLines = periodLinesAll
            .Where(x => !internalTransferEntryIds.Contains(x.EntryId))
            .ToList();

        // Direct-method cash flow: prepaid rent uses actual cash paid (e.g. multi-month advance); do not scale to P&amp;L rent expense.
        var directDetailsRaw = BuildDirectCashFlowDetailRows(periodLines, internalTransferEntryIds, pl);
        var directDetails = ApplyPlOperatingExpenseCashShortfallTopUp(periodLines, pl, directDetailsRaw);

        var operatingTotal = SumDirectByKeys(
            directDetails,
            "sales",
            "prepaidRent",
            "salaries",
            "operatingUtilities",
            "operatingStationery",
            "operatingSuppliesExpense",
            "operatingOtherInflow",
            "operatingOtherOutflow",
            "operatingInventory",
            "operatingOfficeSupplies");
        var investingTotal = SumDirectByKeys(directDetails, "equipment");
        var financingTotal = SumDirectByKeys(directDetails, "ownerInvestment", "loansReceived", "financingOther");
        DateTime? openingTo = from.HasValue ? from.Value.Date.AddDays(-1) : null;
        var openingCashBalance = FilterLines(bid, null, openingTo, stationFilter, incomeStatementJournalMode)
            .AsEnumerable()
            .Where(x => !AccountingDashboardFinance.IsTemporaryChartAccount(x.AccountType))
            .Where(x => string.Equals(x.AccountType, "Asset", StringComparison.OrdinalIgnoreCase) && IsLikelyCashOrBankAsset(x.AccountName))
            .Sum(x => x.Debit - x.Credit);
        // Net cash and ending balance from activity lines only (opening + O+I+F); do not override ending from BS.
        var netIncreaseInCash = operatingTotal + investingTotal + financingTotal;
        var endingCashBalance = openingCashBalance + netIncreaseInCash;

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
                netIncome = balanceSheetNetIncomePlug,
                assets = bs.AssetAccounts,
                liabilities = bs.LiabilityAccounts,
                equity = bs.EquityAccounts,
            },
            cashFlowStatement = new
            {
                method = "direct",
                openingCashBalance,
                directDetails = directDetails
                    .OrderBy(x => x.LineKey)
                    .ThenBy(x => x.AccountCode)
                    .Select(x => new { lineKey = x.LineKey, accountCode = x.AccountCode, accountName = x.AccountName, amount = x.Amount })
                    .ToList(),
                receivedAccounts = pl.IncomeAccounts,
                paidAccounts = pl.ExpenseAccounts,
                cashReceivedFromFuelSales = SumDirectByKeys(directDetails, "sales", "operatingOtherInflow"),
                cashPaidForExpense = SumDirectByKeys(
                    directDetails,
                    "prepaidRent",
                    "salaries",
                    "operatingUtilities",
                    "operatingStationery",
                    "operatingSuppliesExpense",
                    "operatingOtherOutflow",
                    "operatingInventory",
                    "operatingOfficeSupplies"),
                netCashFromOperating = operatingTotal,
                netCashFromInvesting = investingTotal,
                netCashFromFinancing = financingTotal,
                netIncreaseInCash,
                endingCashBalance,
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
            && !AccountingDashboardFinance.IsTemporaryChartAccount(x.AccountType);

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
            .Where(x => !AccountingDashboardFinance.IsTemporaryChartAccount(x.AccountType))
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
                CustomerId = g.Key,
                GivenAmount = g.Sum(x => x.l.Debit),
                PaidAmount = g.Sum(x => x.l.Credit),
                Balance = g.Sum(x => x.l.Debit - x.l.Credit),
            })
            .ToListAsync();

        var customerIds = aggregates.Select(x => x.CustomerId).ToList();
        var names = await db.Customers.AsNoTracking()
            .Where(c => !c.IsDeleted && customerIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name);

        var rows = aggregates
            .Select(x => new
            {
                code,
                customer = names.GetValueOrDefault(x.CustomerId) ?? $"Customer #{x.CustomerId}",
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

