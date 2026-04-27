using backend.Models;

namespace backend.Data.Interfaces;

public interface IDippingPumpRepository
{
    Task<int?> GetDippingIdByNozzleIdAsync(int nozzleId);
    Task<DippingPump?> GetFirstByNozzleIdAsync(int nozzleId);
    Task<DippingPump> AddAsync(DippingPump entity);
    Task<DippingPump> UpdateAsync(int id, DippingPump entity);
    Task SoftDeleteByNozzleIdAsync(int nozzleId);
}
