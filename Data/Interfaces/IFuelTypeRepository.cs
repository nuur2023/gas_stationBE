using gas_station.Models;

namespace gas_station.Data.Interfaces;

public interface IFuelTypeRepository : IGasStationInterface<FuelType>
{
    Task<List<FuelType>> GetAllAsync();
    Task<FuelType?> GetByIdAsync(int id);
}
