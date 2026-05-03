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
                await ProcessDueAsync(db, stoppingToken, logger);
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

    internal static async Task ProcessDueAsync(
        GasStationDBContext db,
        CancellationToken cancellationToken,
        ILogger<RecurringJournalHostedService>? logger = null)
    {
        var today = DateTime.UtcNow.Date;

        var silentList = await db.RecurringJournalEntries
            .Include(x => x.DebitAccount).ThenInclude(a => a.ChartsOfAccounts!)
            .Include(x => x.CreditAccount).ThenInclude(a => a.ChartsOfAccounts!)
            .Where(x => !x.IsDeleted && x.AutoPost && !x.IsPaused && !x.ConfirmWhenDue
                                         && x.NextRunDate != null
                                         && x.NextRunDate.Value.Date <= today
                                         && x.StartDate.Date <= today
                                         && (x.EndDate == null || x.EndDate.Value.Date >= today))
            .ToListAsync(cancellationToken);

        foreach (var r in silentList)
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

            try
            {
                await RecurringJournalPostingHelper.PostAndAdvanceAsync(
                    db,
                    r,
                    runDate,
                    r.Amount,
                    JournalEntryKind.RecurringAuto,
                    cancellationToken);
            }
            catch (InvalidOperationException ex)
            {
                logger?.LogWarning(
                    ex,
                    "Recurring journal entry {RecurringId} was not posted this cycle: {Message}",
                    r.Id,
                    ex.Message);
            }
        }

        var confirmMarkList = await db.RecurringJournalEntries
            .Where(x => !x.IsDeleted && x.AutoPost && !x.IsPaused && x.ConfirmWhenDue
                                         && x.PendingConfirmationRunDate == null
                                         && x.NextRunDate != null
                                         && x.NextRunDate.Value.Date <= today
                                         && x.StartDate.Date <= today
                                         && (x.EndDate == null || x.EndDate.Value.Date >= today))
            .ToListAsync(cancellationToken);

        foreach (var r in confirmMarkList)
        {
            var runDate = r.NextRunDate!.Value;
            // Do not skip when the period is closed — user confirms later; confirm-post enforces the period guard.
            r.PendingConfirmationRunDate = runDate;
            r.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
