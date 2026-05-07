namespace gas_station.ViewModels;

public class AccountWriteRequestViewModel
{
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";
    public int ChartsOfAccountsId { get; set; }
    public int? ParentAccountId { get; set; }
    /// <summary>Null or omitted: global parent account (SuperAdmin only). Set for business-scoped sub-accounts.</summary>
    public int? BusinessId { get; set; }
}

public class JournalEntryLineWriteRequestViewModel
{
    public int AccountId { get; set; }
    public string Debit { get; set; } = "0";
    public string Credit { get; set; } = "0";
    public string? Remark { get; set; }
    /// <summary>FK to CustomerFuelGivens (AR subledger).</summary>
    public int? CustomerId { get; set; }
    public int? SupplierId { get; set; }
}

public class JournalEntryWriteRequestViewModel
{
    public DateTimeOffset? Date { get; set; }
    public string Description { get; set; } = "";
    public int BusinessId { get; set; }
    public int? StationId { get; set; }
    /// <summary>Optional: 0 Normal, 1 Adjusting, 2 Closing, 3 RecurringAuto. Defaults to Normal.</summary>
    public byte? EntryKind { get; set; }
    public List<JournalEntryLineWriteRequestViewModel> Lines { get; set; } = new();
}

/// <summary>Updates journal header fields only; lines and amounts are unchanged.</summary>
public class JournalEntryDescriptionPatchViewModel
{
    public string Description { get; set; } = "";
    public DateTimeOffset? Date { get; set; }
}

/// <summary>Mark an open period closed after books are closed manually (no journal is posted).</summary>
public class MarkAccountingPeriodClosedViewModel
{
    /// <summary>Optional manual close journal id for audit (must belong to the same business).</summary>
    public int? CloseJournalEntryId { get; set; }
}

public class CustomerPaymentWriteRequestViewModel
{
    public int CustomerFuelGivenId { get; set; }
    public string AmountPaid { get; set; } = "0";
    public DateTimeOffset? PaymentDate { get; set; }
    public int BusinessId { get; set; }
}

