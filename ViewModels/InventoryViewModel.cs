namespace gas_station.ViewModels;

/// <summary>API list/detail row: one nozzle line joined to its parent sale (reference + evidence on sale only).</summary>
public class InventoryResponseDto
{
    public int Id { get; set; }
    public int InventorySaleId { get; set; }
    public string ReferenceNumber { get; set; } = string.Empty;
    public string? EvidenceFilePath { get; set; }
    public int NozzleId { get; set; }
    public double OpeningLiters { get; set; }
    public double ClosingLiters { get; set; }
    public double UsageLiters { get; set; }
    public double SspLiters { get; set; }
    public double UsdLiters { get; set; }
    public double SspAmount { get; set; }
    public double UsdAmount { get; set; }
    public double SspFuelPrice { get; set; }
    public double UsdFuelPrice { get; set; }
    public double ExchangeRate { get; set; }
    public int UserId { get; set; }
    public string? UserName { get; set; }
    public DateTime Date { get; set; }
    public int BusinessId { get; set; }
    public int StationId { get; set; }
}

public class InventoryBatchLineRequestViewModel
{
    public int NozzleId { get; set; }
    public string OpeningLiters { get; set; } = "0";
    public string ClosingLiters { get; set; } = "0";
    public string SspLiters { get; set; } = "0";
    public string UsdLiters { get; set; } = "0";
}

public class InventoryBatchCreatePayloadViewModel
{
    public int BusinessId { get; set; }
    public int StationId { get; set; }
    public DateTimeOffset? RecordedAt { get; set; }
    public List<InventoryBatchLineRequestViewModel> Lines { get; set; } = new();
}

public class InventorySaleDetailDto
{
    public int SaleId { get; set; }
    public string ReferenceNumber { get; set; } = string.Empty;
    public int BusinessId { get; set; }
    public int StationId { get; set; }
    public int UserId { get; set; }
    public string? UserName { get; set; }
    public DateTime RecordedDate { get; set; }
    public string? EvidenceFilePath { get; set; }
    public string? OriginalFileName { get; set; }
    public List<InventoryResponseDto> Items { get; set; } = new();
}

public class LatestInventoryForPumpDto
{
    public double? OpeningLiters { get; set; }
    public double? ClosingLiters { get; set; }
    public double? UsageLiters { get; set; }
}

public class InventoryViewModel
{
    public int Id { get; set; }
    public int NozzleId { get; set; }
    public double OpeningLiters { get; set; }
    public double ClosingLiters { get; set; }
    public double UsageLiters { get; set; }
    public DateTime Date { get; set; }
    public int BusinessId { get; set; }
    public int StationId { get; set; }
}
