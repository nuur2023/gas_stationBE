using gas_station.Models;

namespace gas_station.Data.Interfaces;

public interface IFuelTypeRepository : IGasStationInterface<FuelType>
{
    /// <param name="businessId">When set, only fuel types for that business; when null, all (non-deleted).</param>
    Task<List<FuelType>> GetAllAsync(int? businessId = null);
    Task<FuelType?> GetByIdAsync(int id);
}
