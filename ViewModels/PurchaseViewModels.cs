namespace backend.ViewModels;

public class SupplierWriteRequestViewModel
{
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public string? Address { get; set; } = "";
    public string? Email { get; set; } = "";
    public int BusinessId { get; set; }
}

public class PurchaseItemWriteRequestViewModel
{
    public int FuelTypeId { get; set; }
    public string Liters { get; set; } = "0";
    public string PricePerLiter { get; set; } = "0";
    public string TotalAmount { get; set; } = "0";
}

public class PurchaseWriteRequestViewModel
{
    public int SupplierId { get; set; }
    public string InvoiceNo { get; set; } = "";
    public int BusinessId { get; set; }
    public DateTimeOffset? PurchaseDate { get; set; }
    public string Status { get; set; } = "Unpaid";
    public string AmountPaid { get; set; } = "0";
    /// <summary>Optional on create; line items are managed via purchase detail.</summary>
    public List<PurchaseItemWriteRequestViewModel>? Items { get; set; }
}

/// <summary>Update purchase header only (supplier, invoice, date, business).</summary>
public class PurchaseHeaderWriteRequestViewModel
{
    public int SupplierId { get; set; }
    public string InvoiceNo { get; set; } = "";
    public int BusinessId { get; set; }
    public DateTimeOffset? PurchaseDate { get; set; }
    public string Status { get; set; } = "Unpaid";
    public string AmountPaid { get; set; } = "0";
}

/// <summary>API shape: purchase header plus line items.</summary>
public class PurchaseDetailResponse
{
    public int Id { get; set; }
    public int SupplierId { get; set; }
    public string InvoiceNo { get; set; } = "";
    public int BusinessId { get; set; }
    public DateTime PurchaseDate { get; set; }
    public string Status { get; set; } = "Unpaid";
    public double AmountPaid { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<PurchaseItemResponse> Items { get; set; } = [];
}

public class PurchaseItemResponse
{
    public int Id { get; set; }
    public int PurchaseId { get; set; }
    public int FuelTypeId { get; set; }
    public double Liters { get; set; }
    public double PricePerLiter { get; set; }
    public double TotalAmount { get; set; }
    /// <summary>Soft-deleted lines are still returned so the UI can show or restore them.</summary>
    public bool IsDeleted { get; set; }
}
