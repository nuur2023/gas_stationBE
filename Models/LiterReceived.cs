namespace gas_station.Models;

public class LiterReceived : BaseModel
{
    /// <summary>In = fuel into this station (dipping +). Out = fuel leaves this station (dipping −).</summary>
    public string Type { get; set; } = "In";

    /// <summary>Vehicle / cargo reference (targo).</summary>
    public string Targo { get; set; } = string.Empty;

    public string DriverName { get; set; } = string.Empty;

    /// <summary>Legacy search field; mirrored from driver name when saving.</summary>
    public string Name { get; set; } = string.Empty;

    public int FuelTypeId { get; set; }
    public double ReceivedLiter { get; set; }

    /// <summary>Station whose dipping balance is adjusted (receiver for In, sender for Out).</summary>
    public int StationId { get; set; }

    /// <summary>For Out: the other station in the same business (receiving spare fuel).</summary>
    public int? ToStationId { get; set; }

    /// <summary>For In: optional origin station (e.g. supplier depot / other site). Does not affect dipping.</summary>
    public int? FromStationId { get; set; }

    public int BusinessId { get; set; }
    public int UserId { get; set; }

    public int? ViewerTypeId { get; set; }
    public LiterReceivedViewerType? ViewerType { get; set; }

    public DateTime Date { get; set; } = DateTime.UtcNow;
}
