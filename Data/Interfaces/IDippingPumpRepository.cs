using gas_station.Models;

namespace gas_station.Data.Interfaces;

public interface IDippingPumpRepository
{
    Task<DippingPump?> GetByIdAsync(int id);
    Task<int?> GetDippingIdByNozzleIdAsync(int nozzleId);
    Task<DippingPump?> GetFirstByNozzleIdAsync(int nozzleId);
    Task<DippingPump> AddAsync(DippingPump entity);
    Task<DippingPump> UpdateAsync(int id, DippingPump entity);
    Task SoftDeleteByIdAsync(int id);
    Task SoftDeleteByNozzleIdAsync(int nozzleId);
}
