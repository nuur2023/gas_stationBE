namespace gas_station.Models;

public class Pump : BaseModel
{
    public string PumpNumber { get; set; } = string.Empty;
    public int StationId { get; set; }
    public int BusinessId { get; set; }
    public int UserId { get; set; }
}
