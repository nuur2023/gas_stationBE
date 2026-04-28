namespace gas_station.Models;

public class Rate : BaseModel
{
    public double RateNumber { get; set; }
    public int BusinessId { get; set; }
    public int UsersId { get; set; }
    public DateTime Date { get; set; } = DateTime.UtcNow;
    public bool Active { get; set; }
}
