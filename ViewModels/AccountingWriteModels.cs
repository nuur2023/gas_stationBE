namespace backend.ViewModels;

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
    public List<JournalEntryLineWriteRequestViewModel> Lines { get; set; } = new();
}

public class CustomerPaymentWriteRequestViewModel
{
    public int CustomerFuelGivenId { get; set; }
    public string AmountPaid { get; set; } = "0";
    public DateTimeOffset? PaymentDate { get; set; }
    public int BusinessId { get; set; }
}

