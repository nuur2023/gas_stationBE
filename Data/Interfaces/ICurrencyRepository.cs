using backend.Models;

namespace backend.Data.Interfaces;

public interface ICurrencyRepository : IGasStationInterface<Currency>
{
    Task<List<Currency>> GetAllAsync();
    Task<Currency?> GetByIdAsync(int id);
}
