using gas_station.Common;
using gas_station.Models;
using gas_station.ViewModels;

namespace gas_station.Data.Interfaces;

public interface IPurchaseRepository
{
    Task<Purchase?> GetByIdAsync(int id);
    Task<PurchaseDetailResponse?> GetDetailAsync(int id);
    Task<PagedResult<Purchase>> GetPagedAsync(int page, int pageSize, string? search, int? businessId);
    Task<PurchaseDetailResponse> AddWithItemsAsync(Purchase purchase, IReadOnlyList<PurchaseItem> items, SupplierPayment? supplierPayment);
    Task<PurchaseDetailResponse?> UpdateHeaderAsync(int id, Purchase purchase);
    Task<PurchaseDetailResponse?> AddItemAsync(int purchaseId, PurchaseItem item);
    Task<PurchaseDetailResponse?> UpdateItemAsync(int purchaseId, int itemId, PurchaseItem item);
    Task<PurchaseDetailResponse?> DeleteItemAsync(int purchaseId, int itemId);
    Task DeleteAsync(int id);
}
