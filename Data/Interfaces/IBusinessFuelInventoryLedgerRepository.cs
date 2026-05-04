using gas_station.Common;
using gas_station.Models;
using gas_station.ViewModels;

namespace gas_station.Data.Interfaces;

public interface IBusinessFuelInventoryLedgerRepository
{
    Task<List<BusinessFuelInventoryBalanceDto>> GetBalancesAsync(int businessId);
    Task<PagedResult<BusinessFuelInventoryCreditDto>> GetCreditsPagedAsync(int businessId, int page, int pageSize);
    Task<PagedResult<TransferInventoryDto>> GetTransfersPagedAsync(int businessId, int page, int pageSize, TransferInventoryStatus? status);
    Task<List<TransferPendingConfirmDto>> GetPendingTransfersForConfirmAsync(int businessId, int toStationId, int fuelTypeId);
    Task<BusinessFuelInventoryCreditDto> CreditAsync(int businessId, int fuelTypeId, double liters, DateTime date, int creatorId, string reference, string? note);
    Task<bool> SoftDeleteCreditAsync(int id, int businessId);
    Task<TransferInventoryDto> CreateTransferAsync(int businessId, int fuelTypeId, int toStationId, double liters, DateTime date, int creatorId, string? note);
    Task<TransferInventoryDto?> UpdateTransferAsync(int id, int businessId, int toStationId, double liters, DateTime date, string? note, int userId, string reason);
    Task<bool> SoftDeleteTransferAsync(int id, int businessId, int userId, string reason);
    Task<string?> TryMarkTransferReceivedAsync(int transferId, int businessId, int fuelTypeId, int toStationId, double liters, int userId);

    /// <summary>
    /// For an In liter row: if a pending pool transfer matches the same business, fuel, receiving station, and liters, mark it received; otherwise no-op.
    /// </summary>
    Task<string?> TryAutoCompleteMatchingPendingTransferForLiterInAsync(
        int businessId,
        int fuelTypeId,
        int receivingStationId,
        double liters,
        int userId);
    Task<List<TransferInventoryAuditDto>> GetTransferAuditAsync(int transferId, int businessId);
    Task<PagedResult<TransferInventoryAuditListRowDto>> GetTransferAuditsPagedForBusinessAsync(int businessId, int page, int pageSize, string? q);
}
