namespace gas_station.Models;

/// <summary>Lookup for liter-received "viewer type" (mobile + API); seeded rows, editable in DB.</summary>
public class LiterReceivedViewerType : BaseModel
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Stable key for client rules (e.g. <c>our_turn_fare</c>).</summary>
    public string Code { get; set; } = string.Empty;
}
