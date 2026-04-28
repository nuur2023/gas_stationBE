using gas_station.Models;

namespace gas_station.Data.Interfaces;

public interface IFuelPriceRepository : IGasStationInterface<FuelPrice>
{
    Task<List<FuelPrice>> GetAllAsync();
    Task<FuelPrice?> GetByIdAsync(int id);
}
