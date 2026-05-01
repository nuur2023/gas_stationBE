using gas_station.Common;
using gas_station.ViewModels;

namespace gas_station.Data.Interfaces;

public interface IBusinessFuelInventoryLedgerRepository
{
    Task<List<BusinessFuelInventoryBalanceDto>> GetBalancesAsync(int businessId);
    Task<PagedResult<BusinessFuelInventoryCreditDto>> GetCreditsPagedAsync(int businessId, int page, int pageSize);
    Task<PagedResult<TransferInventoryDto>> GetTransfersPagedAsync(int businessId, int page, int pageSize);
    Task<BusinessFuelInventoryCreditDto> CreditAsync(int businessId, int fuelTypeId, double liters, DateTime date, int creatorId, string reference, string? note);
    Task<bool> SoftDeleteCreditAsync(int id, int businessId);
    Task<TransferInventoryDto> CreateTransferAsync(int businessId, int fuelTypeId, int toStationId, double liters, DateTime date, int creatorId, string? note);
    Task<TransferInventoryDto?> UpdateTransferAsync(int id, int businessId, int toStationId, double liters, DateTime date, string? note, int userId, string reason);
    Task<bool> SoftDeleteTransferAsync(int id, int businessId, int userId, string reason);
    Task<List<TransferInventoryAuditDto>> GetTransferAuditAsync(int transferId, int businessId);
}
