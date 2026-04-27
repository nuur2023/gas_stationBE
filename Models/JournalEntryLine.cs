using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace backend.Models;

public class JournalEntryLine : BaseModel
{
    public int JournalEntryId { get; set; }
    [ValidateNever]
    public JournalEntry JournalEntry { get; set; } = null!;

    public int AccountId { get; set; }
    [ValidateNever]
    public Account Account { get; set; } = null!;

    public double Debit { get; set; }
    public double Credit { get; set; }
    public string? Remark { get; set; }

    /// <summary>Subledger: links line to a customer credit record (CustomerFuelGivens) when account is AR.</summary>
    public int? CustomerId { get; set; }
    [ValidateNever]
    public CustomerFuelGiven? Customer { get; set; }

    /// <summary>Subledger: links line to a supplier when account is AP.</summary>
    public int? SupplierId { get; set; }
    [ValidateNever]
    public Supplier? Supplier { get; set; }
}

