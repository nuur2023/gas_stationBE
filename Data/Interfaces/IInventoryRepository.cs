using gas_station.Common;
using gas_station.Models;
using gas_station.ViewModels;

namespace gas_station.Data.Interfaces;

public interface IInventoryRepository
{
    Task<List<InventoryResponseDto>> GetAllAsync();
    Task<InventoryResponseDto?> GetByIdAsync(int id);
    Task<PagedResult<InventoryResponseDto>> GetPagedAsync(int page, int pageSize, string? search, int? businessId, int? stationId);
    Task<InventoryResponseDto?> GetLatestByNozzleIdAsync(int nozzleId);
    Task<InventoryItem> UpdateItemAsync(int id, InventoryItem entity);
    Task<InventoryItem> DeleteItemAsync(int id);
}
