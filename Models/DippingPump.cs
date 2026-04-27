namespace backend.Models;

/// <summary>Links a nozzle to a dipping (tank) for fuel type / balance.</summary>
public class DippingPump : BaseModel
{
    public int NozzleId { get; set; }
    public int DippingId { get; set; }
    public int StationId { get; set; }
    public int BusinessId { get; set; }
    public int UserId { get; set; }
}
