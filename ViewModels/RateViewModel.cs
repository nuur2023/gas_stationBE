namespace gas_station.ViewModels;

/// <summary>Rate row for list APIs; includes creator display name from Users.</summary>
public class RateViewModel
{
    public int Id { get; set; }
    public double RateNumber { get; set; }
    public int BusinessId { get; set; }
    public int UsersId { get; set; }
    public string? UserName { get; set; }
    public DateTime Date { get; set; }
    public bool Active { get; set; }
}
