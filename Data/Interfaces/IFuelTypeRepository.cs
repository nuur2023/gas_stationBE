using backend.Models;

namespace backend.Data.Interfaces;

public interface IFuelTypeRepository : IGasStationInterface<FuelType>
{
    Task<List<FuelType>> GetAllAsync();
    Task<FuelType?> GetByIdAsync(int id);
}
