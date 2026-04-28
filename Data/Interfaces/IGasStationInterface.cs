namespace gas_station.Data.Interfaces;

public interface IGasStationInterface<T> where T : class
{
    Task<T> AddAsync(T entity);
    Task<T> UpdateAsync(int id, T entity);
    Task<T> DeleteAsync(int id);
}
