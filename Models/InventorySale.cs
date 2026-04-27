namespace backend.Models;

/// <summary>One saved batch of fuel inventory readings for a station: single reference and evidence file for all nozzles.</summary>
public class InventorySale : BaseModel
{
    public int BusinessId { get; set; }
    public int StationId { get; set; }
    public int UserId { get; set; }

    /// <summary>Business day / record time for the batch (items use the same moment).</summary>
    public DateTime RecordedDate { get; set; }

    /// <summary>Human-readable unique reference (e.g. INV-20260423-B1-S2-0001).</summary>
    public string ReferenceNumber { get; set; } = string.Empty;

    /// <summary>Relative path under content root, e.g. uploads/inventory-evidence/....</summary>
    public string EvidenceFilePath { get; set; } = string.Empty;

    public string? OriginalFileName { get; set; }
}
