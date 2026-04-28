using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace gas_station.Models;

public class GeneratorUsage : BaseModel
{
    public double LtrUsage { get; set; }
    public int UsersId { get; set; }
    public int BusinessId { get; set; }
    public int StationId { get; set; }

    /// <summary>Generator fuel consumed for this business. Nullable for legacy rows before this column existed.</summary>
    public int? FuelTypeId { get; set; }
    [ValidateNever]
    public FuelType? FuelType { get; set; }

    public DateTime Date { get; set; } = DateTime.UtcNow;
}
