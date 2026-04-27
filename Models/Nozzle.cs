namespace backend.Models;

/// <summary>Physical nozzle / hose on a pump (inventory is recorded per nozzle).</summary>
public class Nozzle : BaseModel
{
    public string Name { get; set; } = string.Empty;
    public int PumpId { get; set; }
    public int StationId { get; set; }
    public int BusinessId { get; set; }
    public int UserId { get; set; }
}
