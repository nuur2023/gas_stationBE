namespace gas_station.ViewModels;

public class AppNotificationDto
{
    public int Id { get; set; }
    public int BusinessId { get; set; }
    public int StationId { get; set; }
    public string StationName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public bool IsRead { get; set; }
    public int? TransferInventoryId { get; set; }
    public string? ConfirmedByName { get; set; }
    public double Liters { get; set; }
    public string FuelName { get; set; } = string.Empty;
    public DateTime TransferDate { get; set; }
}

public class TransferPendingConfirmDto
{
    public int Id { get; set; }
    public double Liters { get; set; }
    public DateTime Date { get; set; }
    public string FuelName { get; set; } = string.Empty;
    public string StationName { get; set; } = string.Empty;
}
