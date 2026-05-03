using gas_station.Data.Context;
using gas_station.Models;
using Microsoft.EntityFrameworkCore;

namespace gas_station.Common;

/// <summary>
/// Validates that recurring postings do not drive a credit-side asset below zero
/// (net debit must cover the credit amount).
/// </summary>
public static class RecurringJournalBalanceGuard
{
    private const double Epsilon = 1e-4;

    /// <summary>
    /// When the recurring template <strong>credits</strong> an <strong>Asset</strong>, the credit reduces net asset.
    /// Require net (debit − credit) through the run date to be at least the credit amount.
    /// </summary>
    public static async Task ValidateBeforeRecurringPostAsync(
        GasStationDBContext db,
        int businessId,
        int? recurringStationId,
        DateTime runDate,
        Account creditAccount,
        double creditAmount,
        CancellationToken cancellationToken = default)
    {
        if (creditAmount <= Epsilon) return;

        var type = creditAccount.ChartsOfAccounts?.Type?.Trim() ?? "";
        if (!string.Equals(type, "Asset", StringComparison.OrdinalIgnoreCase))
            return;

        var available = await GetAccountDebitMinusCreditThroughDateAsync(
            db,
            businessId,
            recurringStationId,
            creditAccount.Id,
            runDate,
            cancellationToken);

        if (available + Epsilon < creditAmount)
        {
            throw new InvalidOperationException(
                $"Insufficient balance on credit account \"{creditAccount.Code} {creditAccount.Name}\". " +
                $"Available (net debit − credit) through {runDate:yyyy-MM-dd} is {available:0.##}; " +
                $"this posting credits {creditAmount:0.##}. Fund the asset (e.g. prepaid) or lower the amount.");
        }
    }

    private static async Task<double> GetAccountDebitMinusCreditThroughDateAsync(
        GasStationDBContext db,
        int businessId,
        int? recurringStationId,
        int accountId,
        DateTime runDateInclusive,
        CancellationToken cancellationToken)
    {
        var runEnd = runDateInclusive.Date.AddDays(1).AddTicks(-1);

        var query =
            from l in db.JournalEntryLines.AsNoTracking()
            where !l.IsDeleted && l.AccountId == accountId
            join e in db.JournalEntries.AsNoTracking() on l.JournalEntryId equals e.Id
            where !e.IsDeleted
                  && e.BusinessId == businessId
                  && e.EntryKind != JournalEntryKind.Closing
                  && e.Date <= runEnd
            select new { l.Debit, l.Credit, e.StationId };

        if (recurringStationId is > 0)
            return await query
                .Where(x => x.StationId == recurringStationId)
                .SumAsync(x => x.Debit - x.Credit, cancellationToken);

        return await query.SumAsync(x => x.Debit - x.Credit, cancellationToken);
    }
}
