using gas_station.Models;

namespace gas_station.Data.Interfaces;

public interface ICurrencyRepository : IGasStationInterface<Currency>
{
    Task<List<Currency>> GetAllAsync();
    Task<Currency?> GetByIdAsync(int id);
}
