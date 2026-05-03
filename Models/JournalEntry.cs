using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace gas_station.Models;

public class JournalEntry : BaseModel
{
    public DateTime Date { get; set; } = DateTime.UtcNow;
    public string Description { get; set; } = string.Empty;

    public int BusinessId { get; set; }
    public int UserId { get; set; }
    public int? StationId { get; set; }

    public JournalEntryKind EntryKind { get; set; } = JournalEntryKind.Normal;
    public int? RecurringJournalEntryId { get; set; }

    [ValidateNever]
    public ICollection<JournalEntryLine> Lines { get; set; } = new List<JournalEntryLine>();
}

