using gas_station.Data.Context;
using gas_station.Models;
using Microsoft.EntityFrameworkCore;

namespace gas_station.Common;

/// <summary>Shared posting logic for recurring journal templates (hosted job and manual confirm).</summary>
public static class RecurringJournalPostingHelper
{
    public static async Task PostAndAdvanceAsync(
        GasStationDBContext db,
        RecurringJournalEntry r,
        DateTime runDate,
        double amt,
        JournalEntryKind kind,
        CancellationToken cancellationToken = default)
    {
        if (amt <= 0) return;

        int? cust = r.CustomerFuelGivenId;
        int? supp = r.SupplierId;
        if (AccountingSubledgerRules.IsAccountsReceivable(r.DebitAccount) && cust is null or <= 0)
            throw new InvalidOperationException("Customer is required for this debit account.");
        if (AccountingSubledgerRules.IsAccountsPayable(r.DebitAccount) && supp is null or <= 0)
            throw new InvalidOperationException("Supplier is required for this debit account.");
        if (!AccountingSubledgerRules.IsAccountsReceivable(r.DebitAccount) && cust is > 0)
            cust = null;

        var debitType = r.DebitAccount.ChartsOfAccounts?.Type ?? "";
        var allowSupplierOnDebit = AccountingSubledgerRules.IsAccountsPayable(r.DebitAccount)
                                   || string.Equals(debitType, "Expense", StringComparison.OrdinalIgnoreCase)
                                   || string.Equals(debitType, "COGS", StringComparison.OrdinalIgnoreCase);
        if (!allowSupplierOnDebit && supp is > 0)
            supp = null;

        var lines = new List<(int accountId, double debit, double credit, string? remark, int? customerId, int? supplierId)>
        {
            (r.DebitAccountId, amt, 0, r.Name, cust, supp),
            (r.CreditAccountId, 0, amt, r.Name, null, null),
        };

        await RecurringJournalBalanceGuard.ValidateBeforeRecurringPostAsync(
            db,
            r.BusinessId,
            r.StationId,
            runDate,
            r.CreditAccount,
            amt,
            cancellationToken);

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await AccountingPostingHelper.CreateJournalEntryAsync(
                db,
                runDate,
                $"Recurring: {r.Name}",
                r.BusinessId,
                r.PostingUserId,
                r.StationId,
                lines,
                kind,
                r.Id);

            var today = DateTime.UtcNow.Date;
            r.LastRunDate = today;
            r.NextRunDate = RecurringJournalSchedule.ComputeNextRunUtc(r.Frequency, runDate);
            r.PendingConfirmationRunDate = null;
            r.Amount = amt;
            r.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
