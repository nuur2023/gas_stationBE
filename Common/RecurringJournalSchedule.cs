using gas_station.Models;

namespace gas_station.Common;

public static class RecurringJournalSchedule
{
    public static DateTime ComputeNextRunUtc(RecurringJournalFrequency frequency, DateTime fromUtcDate)
    {
        var d = fromUtcDate.Date;
        return frequency switch
        {
            RecurringJournalFrequency.Daily => d.AddDays(1),
            RecurringJournalFrequency.Weekly => d.AddDays(7),
            RecurringJournalFrequency.Monthly => d.AddMonths(1),
            RecurringJournalFrequency.Yearly => d.AddYears(1),
            _ => d.AddMonths(1),
        };
    }
}
