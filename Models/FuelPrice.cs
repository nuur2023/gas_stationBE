using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace gas_station.Models;

public class FuelPrice : BaseModel
{
    public int FuelTypeId { get; set; }
    [ValidateNever]
    public FuelType FuelType { get; set; } = null!;

    public int StationId { get; set; }
    [ValidateNever]
    public Station Station { get; set; } = null!;
    public int BusinessId { get; set; }

    public double Price { get; set; }

    public int CurrencyId { get; set; }
    [ValidateNever]
    public Currency Currency { get; set; } = null!;
}
