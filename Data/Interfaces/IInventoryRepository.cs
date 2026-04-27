using backend.Common;
using backend.Models;
using backend.ViewModels;

namespace backend.Data.Interfaces;

public interface IInventoryRepository
{
    Task<List<InventoryResponseDto>> GetAllAsync();
    Task<InventoryResponseDto?> GetByIdAsync(int id);
    Task<PagedResult<InventoryResponseDto>> GetPagedAsync(int page, int pageSize, string? search, int? businessId, int? stationId);
    Task<InventoryResponseDto?> GetLatestByNozzleIdAsync(int nozzleId);
    Task<InventoryItem> UpdateItemAsync(int id, InventoryItem entity);
    Task<InventoryItem> DeleteItemAsync(int id);
}
