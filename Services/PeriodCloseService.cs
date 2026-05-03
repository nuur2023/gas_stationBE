using gas_station.Common;
using gas_station.Data.Context;
using gas_station.Models;
using Microsoft.EntityFrameworkCore;

namespace gas_station.Services;

public sealed class PeriodCloseService(GasStationDBContext db)
{
    private static bool IsIncomeType(string? t) =>
        string.Equals(t, "Income", StringComparison.OrdinalIgnoreCase);

    private static bool IsCogsType(string? t) =>
        string.Equals(t, "COGS", StringComparison.OrdinalIgnoreCase)
        || string.Equals(t, "Cogs", StringComparison.OrdinalIgnoreCase)
        || string.Equals(t, "Cost of Goods Sold", StringComparison.OrdinalIgnoreCase);

    private static bool IsExpenseType(string? t) =>
        string.Equals(t, "Expense", StringComparison.OrdinalIgnoreCase);

    /// <summary>Builds closing journal and marks period closed. Returns journal id or error.</summary>
    public async Task<(bool Ok, string? Error, int? JournalId)> ClosePeriodAsync(
        int periodId,
        int actingUserId,
        CancellationToken cancellationToken = default)
    {
        var period = await db.AccountingPeriods
            .FirstOrDefaultAsync(p => p.Id == periodId && !p.IsDeleted, cancellationToken);
        if (period is null) return (false, "Period not found.", null);
        if (period.Status != AccountingPeriodStatus.Open) return (false, "Period is not open.", null);

        var business = await db.Businesses.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == period.BusinessId && !b.IsDeleted, cancellationToken);
        if (business?.RetainedEarningsAccountId is null or <= 0)
            return (false, "Set RetainedEarningsAccountId on the business before closing.", null);

        var reAccountId = business.RetainedEarningsAccountId.Value;

        var rangeStart = period.PeriodStart.Date;
        var rangeEnd = period.PeriodEnd.Date.AddDays(1).AddTicks(-1);

        var raw = await (
                from l in db.JournalEntryLines.AsNoTracking()
                where !l.IsDeleted
                join e in db.JournalEntries.AsNoTracking() on l.JournalEntryId equals e.Id
                where !e.IsDeleted && e.BusinessId == period.BusinessId && e.Date >= rangeStart && e.Date <= rangeEnd && e.EntryKind != JournalEntryKind.Closing
                join a in db.Accounts.AsNoTracking() on l.AccountId equals a.Id
                where !a.IsDeleted
                join c in db.ChartsOfAccounts.AsNoTracking() on a.ChartsOfAccountsId equals c.Id
                where !c.IsDeleted
                select new { l.Debit, l.Credit, a.Id, c.Type })
            .ToListAsync(cancellationToken);

        var byAccount = raw
            .GroupBy(x => new { x.Id, x.Type })
            .Select(g => new
            {
                g.Key.Id,
                g.Key.Type,
                Debit = g.Sum(x => x.Debit),
                Credit = g.Sum(x => x.Credit),
            })
            .ToList();

        var lines = new List<(int accountId, double debit, double credit, string? remark, int? customerId, int? supplierId)>();

        foreach (var row in byAccount)
        {
            var t = row.Type ?? "";
            if (IsIncomeType(t))
            {
                var netCredit = row.Credit - row.Debit;
                if (Math.Abs(netCredit) < 0.000001) continue;
                lines.Add((row.Id, netCredit, 0, "Period close — clear income", null, null));
            }
            else if (IsCogsType(t) || IsExpenseType(t))
            {
                var netDebit = row.Debit - row.Credit;
                if (Math.Abs(netDebit) < 0.000001) continue;
                lines.Add((row.Id, 0, netDebit, "Period close — clear expense/COGS", null, null));
            }
        }

        var totalDr = lines.Sum(x => x.debit);
        var totalCr = lines.Sum(x => x.credit);
        var diff = totalDr - totalCr;
        if (Math.Abs(diff) > 0.000001)
        {
            if (diff > 0)
                lines.Add((reAccountId, 0, diff, "Period close — net to retained earnings", null, null));
            else
                lines.Add((reAccountId, -diff, 0, "Period close — net to retained earnings", null, null));
        }

        if (lines.Count == 0)
            return (false, "No income or expense/COGS balances to close in this period.", null);

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var journal = await AccountingPostingHelper.CreateJournalEntryAsync(
                db,
                period.PeriodEnd.Date,
                $"Period close {period.Name}",
                period.BusinessId,
                actingUserId,
                null,
                lines,
                JournalEntryKind.Closing,
                null);

            period.Status = AccountingPeriodStatus.Closed;
            period.ClosedAt = DateTime.UtcNow;
            period.ClosedByUserId = actingUserId;
            period.CloseJournalEntryId = journal.Id;
            period.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return (true, null, journal.Id);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            return (false, ex.Message, null);
        }
    }
}
