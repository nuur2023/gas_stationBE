using gas_station.Common;
using gas_station.Data.Context;
using gas_station.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace gas_station.Services;

/// <summary>Runs periodically and posts due <see cref="RecurringJournalEntry"/> rows as journals.</summary>
public sealed class RecurringJournalHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<RecurringJournalHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<GasStationDBContext>();
                await ProcessDueAsync(db, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Recurring journal hosted service run failed.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    internal static async Task ProcessDueAsync(GasStationDBContext db, CancellationToken cancellationToken)
    {
        var today = DateTime.UtcNow.Date;
        var list = await db.RecurringJournalEntries
            .Include(x => x.DebitAccount).ThenInclude(a => a.ChartsOfAccounts)
            .Include(x => x.CreditAccount).ThenInclude(a => a.ChartsOfAccounts)
            .Where(x => !x.IsDeleted && x.AutoPost && !x.IsPaused
                                         && x.NextRunDate != null
                                         && x.NextRunDate.Value.Date <= today
                                         && x.StartDate.Date <= today
                                         && (x.EndDate == null || x.EndDate.Value.Date >= today))
            .ToListAsync(cancellationToken);

        foreach (var r in list)
        {
            if (r.LastRunDate?.Date == today)
                continue;

            var runDate = r.NextRunDate!.Value;
            if (await AccountingPeriodGuard.IsPostingBlockedAsync(db, r.BusinessId, runDate, JournalEntryKind.RecurringAuto, cancellationToken))
            {
                r.NextRunDate = RecurringJournalSchedule.ComputeNextRunUtc(r.Frequency, runDate);
                r.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
                continue;
            }

            var amt = r.Amount;
            if (amt <= 0) continue;

            int? cust = r.CustomerFuelGivenId;
            int? supp = r.SupplierId;
            if (AccountingSubledgerRules.IsAccountsReceivable(r.DebitAccount) && cust is null or <= 0)
                continue;
            if (AccountingSubledgerRules.IsAccountsPayable(r.DebitAccount) && supp is null or <= 0)
                continue;
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
                    JournalEntryKind.RecurringAuto,
                    r.Id);

                r.LastRunDate = today;
                r.NextRunDate = RecurringJournalSchedule.ComputeNextRunUtc(r.Frequency, runDate);
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
}
