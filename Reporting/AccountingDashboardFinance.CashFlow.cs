using gas_station.Data.Context;
using gas_station.Models;

namespace gas_station.Reporting;

internal static partial class AccountingDashboardFinance
{
    internal static bool IsLikelyCashOrBankAsset(string? accountName)
    {
        if (string.IsNullOrWhiteSpace(accountName)) return false;
        var n = accountName.Trim();
        return n.Contains("cash", StringComparison.OrdinalIgnoreCase)
            || n.Contains("bank", StringComparison.OrdinalIgnoreCase)
            || n.Contains("petty", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPotentialInternalCashTransferLine(DashJournalLineRow x) =>
        string.Equals(x.AccountType, "Asset", StringComparison.OrdinalIgnoreCase)
        && IsLikelyCashOrBankAsset(x.AccountName);

    private static bool IsCashOrBankAccount(DashJournalLineRow x) =>
        string.Equals(x.AccountType, "Asset", StringComparison.OrdinalIgnoreCase)
        && IsLikelyCashOrBankAsset(x.AccountName);

    private static bool IsInternalCashTransferEntry(IReadOnlyList<DashJournalLineRow> lines)
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

    private static bool NameLooksLikeInventoryAccount(DashJournalLineRow x)
    {
        var n = x.AccountName ?? "";
        return n.Contains("inventory", StringComparison.OrdinalIgnoreCase)
            || n.Contains("laptop", StringComparison.OrdinalIgnoreCase);
    }

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

    private static string? ClassifyCashInflowLineKey(DashJournalLineRow c)
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

    private static string ClassifyCashOutflowLineKey(DashJournalLineRow d)
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

        if (n.Contains("office supply", StringComparison.OrdinalIgnoreCase)
            || (string.Equals(t, "Asset", StringComparison.OrdinalIgnoreCase)
                && n.Contains("suppl", StringComparison.OrdinalIgnoreCase)
                && !n.Contains("inventory", StringComparison.OrdinalIgnoreCase)
                && !n.Contains("prepaid", StringComparison.OrdinalIgnoreCase)))
            return "operatingOfficeSupplies";

        if (n.Contains("salary", StringComparison.OrdinalIgnoreCase) || n.Contains("wage", StringComparison.OrdinalIgnoreCase))
            return "salaries";

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

    private static bool IsAccountsPayableDebitLine(DashJournalLineRow d)
    {
        if (!string.Equals(d.AccountType, "Liability", StringComparison.OrdinalIgnoreCase)) return false;
        var n = d.AccountName ?? "";
        return n.Contains("payable", StringComparison.OrdinalIgnoreCase)
            || n.Contains("a/p", StringComparison.OrdinalIgnoreCase)
            || n.Contains("creditor", StringComparison.OrdinalIgnoreCase);
    }

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

    private static List<PlExpenseWeight> BuildPlOperatingExpenseWeights(DashProfitLossReportData pl)
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

    private static double ExpensePlAmountFromZeroNetCashJournalEntries(
        IReadOnlyList<DashJournalLineRow> periodLines,
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

    private static List<DashDirectCashFlowDetailRow> ApplyPlOperatingExpenseCashShortfallTopUp(
        IReadOnlyList<DashJournalLineRow> periodLines,
        DashProfitLossReportData pl,
        IReadOnlyList<DashDirectCashFlowDetailRow> rows)
    {
        var result = rows.ToList();
        var additions = new List<DashDirectCashFlowDetailRow>();
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
            var row = new DashDirectCashFlowDetailRow(key, code, exp.name ?? "", desired);
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

    private static List<DashDirectCashFlowDetailRow> BuildDirectCashFlowDetailRows(
        IReadOnlyList<DashJournalLineRow> periodLines,
        HashSet<int> internalTransferEntryIds,
        DashProfitLossReportData pl)
    {
        var outRows = new List<DashDirectCashFlowDetailRow>();
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
                        outRows.Add(new DashDirectCashFlowDetailRow(
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
                        outRows.Add(new DashDirectCashFlowDetailRow(
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
                    outRows.Add(new DashDirectCashFlowDetailRow(
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
                            outRows.Add(new DashDirectCashFlowDetailRow(
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
                    outRows.Add(new DashDirectCashFlowDetailRow(
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

    private static double SumDirectByKeys(IEnumerable<DashDirectCashFlowDetailRow> rows, params string[] keys)
    {
        var set = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
        return rows.Where(x => set.Contains(x.LineKey)).Sum(x => x.Amount);
    }

    internal sealed record CashFlowPeriodTotals(double Operating, double Investing, double Financing, double NetIncrease);

    /// <summary>Direct-method cash flow for [from, to] inclusive dates; aligns with FinancialReports report-period-view.</summary>
    internal static CashFlowPeriodTotals ComputeCashFlowTotals(
        GasStationDBContext db,
        int bid,
        DateTime from,
        DateTime to,
        int? stationFilter,
        string? trialBalanceMode)
    {
        var incomeStatementJournalMode = IncomeStatementTrialBalanceMode(trialBalanceMode);
        var pl = BuildProfitLossReportData(db, bid, from, to, stationFilter, incomeStatementJournalMode);

        var periodLinesAll = FilterLines(db, bid, from, to, stationFilter, incomeStatementJournalMode)
            .AsEnumerable()
            .Where(x => !IsTemporaryChartAccount(x.AccountType))
            .ToList();

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
        var netIncreaseInCash = operatingTotal + investingTotal + financingTotal;
        return new CashFlowPeriodTotals(operatingTotal, investingTotal, financingTotal, netIncreaseInCash);
    }
}
