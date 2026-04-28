using gas_station.Models;

namespace gas_station.Data.Interfaces;

public interface INozzleRepository
{
    Task<Nozzle?> GetByIdAsync(int id);
    Task<List<Nozzle>> ListByStationAsync(int stationId, int businessId);
    Task<List<Nozzle>> ListByBusinessAsync(int businessId);
    Task<List<Nozzle>> ListByPumpIdAsync(int pumpId);
    Task<Nozzle> AddAsync(Nozzle entity);
    Task<Nozzle> UpdateAsync(int id, Nozzle entity);
    Task<Nozzle> DeleteAsync(int id);
    Task SoftDeleteByPumpIdAsync(int pumpId);
}
