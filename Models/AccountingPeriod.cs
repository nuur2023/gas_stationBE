namespace gas_station.Models;

/// <summary>Fiscal/book period for close controls and reporting.</summary>
public class AccountingPeriod : BaseModel
{
    public int BusinessId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public AccountingPeriodStatus Status { get; set; } = AccountingPeriodStatus.Open;
    public DateTime? ClosedAt { get; set; }
    public int? ClosedByUserId { get; set; }
    public int? CloseJournalEntryId { get; set; }
}
