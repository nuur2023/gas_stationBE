namespace backend.ViewModels;

public class PumpViewModel
{
    public int Id { get; set; }
    public string PumpNumber { get; set; } = "";
    public string Nozzle { get; set; } = "";
    public int DippingId { get; set; }
    public int StationId { get; set; }
    public int BusinessId { get; set; }
}
