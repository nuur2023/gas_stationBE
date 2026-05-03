using gas_station.Data.Context;
using gas_station.Models;
using Microsoft.EntityFrameworkCore;

namespace gas_station.Common;

public static class AccountingPeriodGuard
{
    /// <summary>Returns true when <paramref name="entryDate"/> falls in a closed or locked period (posting blocked for non-closing entries).</summary>
    public static async Task<bool> IsPostingBlockedAsync(
        GasStationDBContext db,
        int businessId,
        DateTime entryDate,
        JournalEntryKind proposedKind,
        CancellationToken cancellationToken = default)
    {
        if (proposedKind == JournalEntryKind.Closing)
            return false;

        var d = entryDate.Date;
        return await db.AccountingPeriods.AsNoTracking()
            .AnyAsync(p =>
                    !p.IsDeleted &&
                    p.BusinessId == businessId &&
                    p.Status != AccountingPeriodStatus.Open &&
                    p.PeriodStart.Date <= d &&
                    p.PeriodEnd.Date >= d,
                cancellationToken);
    }
}
