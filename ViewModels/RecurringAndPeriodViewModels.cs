namespace gas_station.ViewModels;

public class RecurringJournalEntryWriteViewModel
{
    public int BusinessId { get; set; }
    /// <summary>Optional station for auto-posted journals from this template.</summary>
    public int? StationId { get; set; }
    public string Name { get; set; } = "";
    public int DebitAccountId { get; set; }
    public int CreditAccountId { get; set; }
    public string Amount { get; set; } = "0";
    /// <summary>0 daily, 1 weekly, 2 monthly, 3 yearly.</summary>
    public byte Frequency { get; set; } = 2;
    public DateTimeOffset? StartDate { get; set; }
    public DateTimeOffset? EndDate { get; set; }
    public bool AutoPost { get; set; } = true;
    public bool IsPaused { get; set; }
    public int? SupplierId { get; set; }
    public int? CustomerFuelGivenId { get; set; }
    public int PostingUserId { get; set; }
}

public class AccountingPeriodWriteViewModel
{
    public int BusinessId { get; set; }
    public string Name { get; set; } = "";
    public DateTimeOffset PeriodStart { get; set; }
    public DateTimeOffset PeriodEnd { get; set; }
}
