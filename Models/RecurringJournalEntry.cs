using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace gas_station.Models;

/// <summary>Scheduled template that posts a balanced two-line journal when due.</summary>
public class RecurringJournalEntry : BaseModel
{
    public int BusinessId { get; set; }
    /// <summary>Optional: journal entries created from this template use this station.</summary>
    public int? StationId { get; set; }
    [ValidateNever]
    public Station? Station { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DebitAccountId { get; set; }
    [ValidateNever]
    public Account DebitAccount { get; set; } = null!;
    public int CreditAccountId { get; set; }
    [ValidateNever]
    public Account CreditAccount { get; set; } = null!;
    public double Amount { get; set; }
    public RecurringJournalFrequency Frequency { get; set; } = RecurringJournalFrequency.Monthly;
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool AutoPost { get; set; } = true;
    public bool IsPaused { get; set; }

    /// <summary>
    /// When true, the background job does not post when due; it sets <see cref="PendingConfirmationRunDate"/>
    /// until a user confirms (and may edit the amount) via the API.
    /// </summary>
    public bool ConfirmWhenDue { get; set; }

    /// <summary>Set when this template is waiting for user confirmation for the run at this date.</summary>
    public DateTime? PendingConfirmationRunDate { get; set; }
    public int? SupplierId { get; set; }
    /// <summary>AR subledger: CustomerFuelGiven id when debit line tags a customer.</summary>
    public int? CustomerFuelGivenId { get; set; }
    public DateTime? LastRunDate { get; set; }
    public DateTime? NextRunDate { get; set; }
    /// <summary>User id stored on generated <see cref="JournalEntry"/>.</summary>
    public int PostingUserId { get; set; }
}
