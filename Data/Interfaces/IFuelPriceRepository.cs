using backend.Models;

namespace backend.Data.Interfaces;

public interface IFuelPriceRepository : IGasStationInterface<FuelPrice>
{
    Task<List<FuelPrice>> GetAllAsync();
    Task<FuelPrice?> GetByIdAsync(int id);
}
