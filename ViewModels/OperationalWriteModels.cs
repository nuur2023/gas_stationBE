namespace gas_station.ViewModels;



/// <summary>API body for create/update — avoids model validation on <c>BusinessUser.User</c> / <c>Business</c> navigations.</summary>

public class BusinessUserWriteRequestViewModel

{

    public int UserId { get; set; }

    public int BusinessId { get; set; }

    public int StationId { get; set; }

}



public class ExpenseWriteRequestViewModel

{
    /// <summary>Expense | cashOrUsdTaken | Exchange</summary>
    public string Type { get; set; } = "Expense";
    /// <summary>Operation | Management</summary>
    public string SideAction { get; set; } = "Operation";

    public string Description { get; set; } = "";

    public string CurrencyCode { get; set; } = "USD";

    public string LocalAmount { get; set; } = "0";

    public string Rate { get; set; } = "0";

    public string AmountUsd { get; set; } = "0";

    public int StationId { get; set; }

    public int BusinessId { get; set; }

    public DateTimeOffset? Date { get; set; }

}



public class InventoryWriteRequestViewModel

{

    public int NozzleId { get; set; }

    public int StationId { get; set; }

    public string OpeningLiters { get; set; } = "0";

    public string ClosingLiters { get; set; } = "0";

    /// <summary>Liters attributed to SSP fuel price; must equal usage minus <see cref="UsdLiters"/>.</summary>
    public string SspLiters { get; set; } = "0";

    /// <summary>Liters attributed to USD fuel price.</summary>
    public string UsdLiters { get; set; } = "0";

    public DateTimeOffset? RecordedAt { get; set; }

    public int BusinessId { get; set; }

}



public class RateWriteRequestViewModel

{

    public string RateNumber { get; set; } = "0";

    public bool Active { get; set; } = true;

    public int BusinessId { get; set; }

}



public class GeneratorUsageWriteRequestViewModel

{

    public string LtrUsage { get; set; } = "0";

    public int StationId { get; set; }

    public int FuelTypeId { get; set; }

    public int BusinessId { get; set; }

    public DateTimeOffset? Date { get; set; }

}



public class CustomerFuelGivenWriteRequestViewModel

{

    public string Name { get; set; } = "";

    public string Phone { get; set; } = "";

    public int FuelTypeId { get; set; }

    public string GivenLiter { get; set; } = "0";

    public string Price { get; set; } = "0";

    public string AmountUsd { get; set; } = "0";

    public string? Remark { get; set; }

    public int StationId { get; set; }

    public int BusinessId { get; set; }

    public DateTimeOffset? Date { get; set; }

}



public class PumpWriteRequestViewModel

{

    public string PumpNumber { get; set; } = "";

    public int StationId { get; set; }

    /// <summary>SuperAdmin must set; others use JWT business.</summary>

    public int BusinessId { get; set; }

}

public class NozzleWriteRequestViewModel
{
    public string Name { get; set; } = "";
    public int PumpId { get; set; }
    public int StationId { get; set; }
    public int BusinessId { get; set; }
}

public class DippingPumpWriteRequestViewModel
{
    public int NozzleId { get; set; }
    public int DippingId { get; set; }
    public int StationId { get; set; }
    public int BusinessId { get; set; }
}

public class SupplierPaymentWriteRequestViewModel
{
    public string? ReferenceNo { get; set; }
    public int SupplierId { get; set; }
    public string Amount { get; set; } = "0";
    public DateTimeOffset? Date { get; set; }
    /// <summary>SuperAdmin must set; others use JWT business.</summary>
    public int BusinessId { get; set; }
}

public class StationWriteRequestViewModel

{

    public string Name { get; set; } = "";

    public string Address { get; set; } = "";

    public bool IsActive { get; set; } = true;

    /// <summary>When set and caller is SuperAdmin, station is created/updated under this business. Others use JWT business.</summary>

    public int BusinessId { get; set; }

}



public class DippingWriteRequestViewModel

{

    public string Name { get; set; } = "";

    public int FuelTypeId { get; set; }

    public string AmountLiter { get; set; } = "0";

    public int StationId { get; set; }

    public int BusinessId { get; set; }

}



public class LiterReceivedWriteRequestViewModel

{

    /// <summary>In or Out.</summary>
    public string Type { get; set; } = "In";

    public string Targo { get; set; } = "";

    public string DriverName { get; set; } = "";

    public int FuelTypeId { get; set; }

    public string ReceivedLiter { get; set; } = "0";

    /// <summary>For SuperAdmin In, or Out (source station). Ignored for staff In when JWT has a station.</summary>
    public int StationId { get; set; }

    /// <summary>Required for Out: destination station (same business, not the source).</summary>
    public int? ToStationId { get; set; }

    /// <summary>Optional for In: origin station (same business). Omit or null if unknown.</summary>
    public int? FromStationId { get; set; }

    public int BusinessId { get; set; }

    public DateTimeOffset? RecordedAt { get; set; }

    /// <summary>Out flow only: when true with <see cref="ConfirmTransferInventoryId"/>, marks that pool transfer as received.</summary>
    public bool ConfirmBusinessPoolTransferReceived { get; set; }

    /// <summary>Pending <c>TransferInventory</c> id to mark received (Out flow).</summary>
    public int? ConfirmTransferInventoryId { get; set; }

}

