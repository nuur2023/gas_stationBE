namespace gas_station.ViewModels;

public class BusinessFuelInventoryBalanceDto
{
    public int Id { get; set; }
    public int BusinessId { get; set; }
    public int FuelTypeId { get; set; }
    public string FuelName { get; set; } = string.Empty;
    public double Liters { get; set; }
}

public class BusinessFuelInventoryCreditDto
{
    public int Id { get; set; }
    public int BusinessId { get; set; }
    public int FuelTypeId { get; set; }
    public string FuelName { get; set; } = string.Empty;
    public double Liters { get; set; }
    public DateTime Date { get; set; }
    public int CreatorId { get; set; }
    public string? CreatorName { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string? Note { get; set; }
}

public class TransferInventoryDto
{
    public int Id { get; set; }
    public int BusinessFuelInventoryId { get; set; }
    public int BusinessId { get; set; }
    public int FuelTypeId { get; set; }
    public string FuelName { get; set; } = string.Empty;
    public int ToStationId { get; set; }
    public string StationName { get; set; } = string.Empty;
    public double Liters { get; set; }
    public DateTime Date { get; set; }
    public int CreatorId { get; set; }
    public string? CreatorName { get; set; }
    public string? Note { get; set; }
}

public class TransferInventoryAuditDto
{
    public int Id { get; set; }
    public int TransferInventoryId { get; set; }
    public string Action { get; set; } = string.Empty;
    public DateTime ChangedAt { get; set; }
    public int ChangedByUserId { get; set; }
    public string? ChangedByName { get; set; }
    public int ToStationId { get; set; }
    public double Liters { get; set; }
    public DateTime Date { get; set; }
    public string? Reason { get; set; }
    public int BusinessId { get; set; }
}

/// <summary>Audit row with transfer context for business-wide listing.</summary>
public class TransferInventoryAuditListRowDto
{
    public int Id { get; set; }
    public int TransferInventoryId { get; set; }
    public string Action { get; set; } = string.Empty;
    public DateTime ChangedAt { get; set; }
    public int ChangedByUserId { get; set; }
    public string? ChangedByName { get; set; }
    public int ToStationId { get; set; }
    public double Liters { get; set; }
    public DateTime Date { get; set; }
    public string? Reason { get; set; }
    public int BusinessId { get; set; }
    public string FuelName { get; set; } = string.Empty;
    public string StationName { get; set; } = string.Empty;
}

public class BusinessFuelInventoryCreditWriteRequest
{
    public int BusinessId { get; set; }
    public int FuelTypeId { get; set; }
    public string Liters { get; set; } = "0";
    public DateTimeOffset? Date { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string? Note { get; set; }
}

public class TransferInventoryWriteRequest
{
    public int BusinessId { get; set; }
    public int FuelTypeId { get; set; }
    public int ToStationId { get; set; }
    public string Liters { get; set; } = "0";
    public DateTimeOffset? Date { get; set; }
    public string? Note { get; set; }
}

public class TransferInventoryUpdateRequest
{
    public int BusinessId { get; set; }
    public int ToStationId { get; set; }
    public string Liters { get; set; } = "0";
    public DateTimeOffset? Date { get; set; }
    public string? Note { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class TransferInventoryDeleteRequest
{
    public int BusinessId { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class BusinessFuelInventoryCreditDeleteRequest
{
    public int BusinessId { get; set; }
}
