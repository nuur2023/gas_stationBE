using gas_station.Common;
using gas_station.Data.Context;
using gas_station.Data.Interfaces;
using gas_station.Models;
using gas_station.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace gas_station.Data.Repository;

public class BusinessFuelInventoryLedgerRepository(GasStationDBContext context) : IBusinessFuelInventoryLedgerRepository
{
    private readonly GasStationDBContext _context = context;

    private sealed record TransferSnapshot(int ToStationId, double Liters, DateTime Date, string? Note);

    private static bool LitersMatch(double expected, double actual) =>
        Math.Abs(expected - actual) < 0.000_001;

    public async Task<List<BusinessFuelInventoryBalanceDto>> GetBalancesAsync(int businessId)
    {
        return await (
            from b in _context.BusinessFuelInventories.AsNoTracking()
            join f in _context.FuelTypes.AsNoTracking() on b.FuelTypeId equals f.Id
            where !b.IsDeleted && !f.IsDeleted && b.BusinessId == businessId
            orderby f.FuelName
            select new BusinessFuelInventoryBalanceDto
            {
                Id = b.Id,
                BusinessId = b.BusinessId,
                FuelTypeId = b.FuelTypeId,
                FuelName = f.FuelName,
                Liters = b.Liters,
            }).ToListAsync();
    }

    public async Task<PagedResult<BusinessFuelInventoryCreditDto>> GetCreditsPagedAsync(int businessId, int page, int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var q = from c in _context.BusinessFuelInventoryCredits.AsNoTracking()
            join f in _context.FuelTypes.AsNoTracking() on c.FuelTypeId equals f.Id
            join u in _context.Users.AsNoTracking() on c.CreatorId equals u.Id into uj
            from u in uj.DefaultIfEmpty()
            where !c.IsDeleted && !f.IsDeleted && c.BusinessId == businessId
            orderby c.Date descending, c.Id descending
            select new BusinessFuelInventoryCreditDto
            {
                Id = c.Id,
                BusinessId = c.BusinessId,
                FuelTypeId = c.FuelTypeId,
                FuelName = f.FuelName,
                Liters = c.Liters,
                Date = c.Date,
                CreatorId = c.CreatorId,
                CreatorName = u != null ? u.Name : null,
                Reference = c.Reference,
                Note = c.Note,
            };
        var total = await q.CountAsync();
        var items = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return new PagedResult<BusinessFuelInventoryCreditDto>(items, total, page, pageSize);
    }

    public async Task<PagedResult<TransferInventoryDto>> GetTransfersPagedAsync(
        int businessId,
        int page,
        int pageSize,
        TransferInventoryStatus? status)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var q = from t in _context.TransferInventories.AsNoTracking()
            join b in _context.BusinessFuelInventories.AsNoTracking() on t.BusinessFuelInventoryId equals b.Id
            join f in _context.FuelTypes.AsNoTracking() on b.FuelTypeId equals f.Id
            join s in _context.Stations.AsNoTracking() on t.ToStationId equals s.Id
            join u in _context.Users.AsNoTracking() on t.CreatorId equals u.Id into uj
            from u in uj.DefaultIfEmpty()
            where !t.IsDeleted && !b.IsDeleted && !f.IsDeleted && !s.IsDeleted && b.BusinessId == businessId
            where status == null || t.Status == status
            orderby t.Date descending, t.Id descending
            select new TransferInventoryDto
            {
                Id = t.Id,
                BusinessFuelInventoryId = t.BusinessFuelInventoryId,
                BusinessId = b.BusinessId,
                FuelTypeId = b.FuelTypeId,
                FuelName = f.FuelName,
                ToStationId = t.ToStationId,
                StationName = s.Name,
                Liters = t.Liters,
                Date = t.Date,
                CreatorId = t.CreatorId,
                CreatorName = u != null ? u.Name : null,
                Note = t.Note,
                Status = t.Status,
            };
        var total = await q.CountAsync();
        var items = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return new PagedResult<TransferInventoryDto>(items, total, page, pageSize);
    }

    public async Task<BusinessFuelInventoryCreditDto> CreditAsync(
        int businessId,
        int fuelTypeId,
        double liters,
        DateTime date,
        int creatorId,
        string reference,
        string? note)
    {
        if (liters <= 0) throw new InvalidOperationException("Liters must be greater than zero.");

        var ft = await _context.FuelTypes.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == fuelTypeId && !x.IsDeleted);
        if (ft is null || ft.BusinessId != businessId)
            throw new InvalidOperationException("Fuel type not found or does not belong to this business.");

        await using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            var balance = await _context.BusinessFuelInventories
                .FirstOrDefaultAsync(x => x.BusinessId == businessId && x.FuelTypeId == fuelTypeId && !x.IsDeleted);
            var now = DateTime.UtcNow;
            if (balance is null)
            {
                balance = new BusinessFuelInventory
                {
                    BusinessId = businessId,
                    FuelTypeId = fuelTypeId,
                    Liters = 0,
                    CreatedAt = now,
                    UpdatedAt = now,
                    IsDeleted = false,
                };
                _context.BusinessFuelInventories.Add(balance);
                await _context.SaveChangesAsync();
            }

            balance.Liters += liters;
            balance.UpdatedAt = now;

            var credit = new BusinessFuelInventoryCredit
            {
                BusinessId = businessId,
                FuelTypeId = fuelTypeId,
                Liters = liters,
                Date = date,
                CreatorId = creatorId,
                Reference = reference.Trim(),
                Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
                CreatedAt = now,
                UpdatedAt = now,
                IsDeleted = false,
            };
            _context.BusinessFuelInventoryCredits.Add(credit);
            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            var fuelName = ft.FuelName;
            var creatorName = await _context.Users.AsNoTracking()
                .Where(x => x.Id == creatorId)
                .Select(x => x.Name)
                .FirstOrDefaultAsync();

            return new BusinessFuelInventoryCreditDto
            {
                Id = credit.Id,
                BusinessId = businessId,
                FuelTypeId = fuelTypeId,
                FuelName = fuelName,
                Liters = liters,
                Date = date,
                CreatorId = creatorId,
                CreatorName = creatorName,
                Reference = credit.Reference,
                Note = credit.Note,
            };
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task<bool> SoftDeleteCreditAsync(int id, int businessId)
    {
        await using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            var credit = await _context.BusinessFuelInventoryCredits
                .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
            if (credit is null || credit.BusinessId != businessId) return false;

            var balance = await _context.BusinessFuelInventories
                .FirstOrDefaultAsync(x => x.BusinessId == businessId && x.FuelTypeId == credit.FuelTypeId && !x.IsDeleted);
            if (balance is null || balance.Liters < credit.Liters)
                throw new InvalidOperationException("Cannot delete this credit because the current pool balance is lower than the credited liters.");

            var now = DateTime.UtcNow;
            balance.Liters -= credit.Liters;
            balance.UpdatedAt = now;
            credit.IsDeleted = true;
            credit.UpdatedAt = now;

            await _context.SaveChangesAsync();
            await tx.CommitAsync();
            return true;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task<TransferInventoryDto> CreateTransferAsync(
        int businessId,
        int fuelTypeId,
        int toStationId,
        double liters,
        DateTime date,
        int creatorId,
        string? note)
    {
        if (liters <= 0) throw new InvalidOperationException("Liters must be greater than zero.");

        var ft = await _context.FuelTypes.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == fuelTypeId && !x.IsDeleted);
        if (ft is null || ft.BusinessId != businessId)
            throw new InvalidOperationException("Fuel type not found or does not belong to this business.");

        var st = await _context.Stations.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == toStationId && !x.IsDeleted);
        if (st is null || st.BusinessId != businessId)
            throw new InvalidOperationException("Station not found or does not belong to this business.");

        await using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            var balance = await _context.BusinessFuelInventories
                .FirstOrDefaultAsync(x => x.BusinessId == businessId && x.FuelTypeId == fuelTypeId && !x.IsDeleted);
            var now = DateTime.UtcNow;
            if (balance is null || balance.Liters < liters)
                throw new InvalidOperationException("Insufficient business pool balance for this fuel type.");

            balance.Liters -= liters;
            balance.UpdatedAt = now;

            var transfer = new TransferInventory
            {
                BusinessFuelInventoryId = balance.Id,
                ToStationId = toStationId,
                Liters = liters,
                Date = date,
                CreatorId = creatorId,
                Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
                CreatedAt = now,
                UpdatedAt = now,
                IsDeleted = false,
            };
            _context.TransferInventories.Add(transfer);
            await _context.SaveChangesAsync();

            _context.TransferInventoryAudits.Add(new TransferInventoryAudit
            {
                TransferInventoryId = transfer.Id,
                Action = "Created",
                ChangedAt = now,
                ChangedByUserId = creatorId,
                Reason = null,
                ToStationId = transfer.ToStationId,
                Liters = transfer.Liters,
                Date = transfer.Date,
                BusinessId = businessId,
                CreatedAt = now,
                UpdatedAt = now,
                IsDeleted = false,
            });
            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            return await BuildTransferDtoAsync(transfer.Id, businessId) ?? throw new InvalidOperationException("Transfer not found after create.");
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task<TransferInventoryDto?> UpdateTransferAsync(
        int id,
        int businessId,
        int toStationId,
        double liters,
        DateTime date,
        string? note,
        int userId,
        string reason)
    {
        if (liters <= 0) throw new InvalidOperationException("Liters must be greater than zero.");
        if (string.IsNullOrWhiteSpace(reason)) throw new InvalidOperationException("Reason is required.");

        var st = await _context.Stations.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == toStationId && !x.IsDeleted);
        if (st is null || st.BusinessId != businessId)
            throw new InvalidOperationException("Station not found or does not belong to this business.");

        await using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            var transfer = await _context.TransferInventories
                .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
            if (transfer is null) return null;

            var balance = await _context.BusinessFuelInventories
                .FirstOrDefaultAsync(x => x.Id == transfer.BusinessFuelInventoryId && !x.IsDeleted);
            if (balance is null || balance.BusinessId != businessId) return null;

            var before = new TransferSnapshot(transfer.ToStationId, transfer.Liters, transfer.Date, transfer.Note);
            var oldLiters = transfer.Liters;

            balance.Liters += oldLiters;
            if (balance.Liters < liters)
            {
                balance.Liters -= oldLiters;
                throw new InvalidOperationException("Insufficient business pool balance after reversing this transfer.");
            }

            balance.Liters -= liters;
            var now = DateTime.UtcNow;
            balance.UpdatedAt = now;

            transfer.ToStationId = toStationId;
            transfer.Liters = liters;
            transfer.Date = date;
            transfer.Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
            transfer.UpdatedAt = now;

            _context.TransferInventoryAudits.Add(new TransferInventoryAudit
            {
                TransferInventoryId = transfer.Id,
                Action = "Updated",
                ChangedAt = now,
                ChangedByUserId = userId,
                Reason = reason.Trim(),
                ToStationId = transfer.ToStationId,
                Liters = transfer.Liters,
                Date = transfer.Date,
                BusinessId = businessId,
                CreatedAt = now,
                UpdatedAt = now,
                IsDeleted = false,
            });

            await _context.SaveChangesAsync();
            await tx.CommitAsync();
            return await BuildTransferDtoAsync(transfer.Id, businessId);
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task<bool> SoftDeleteTransferAsync(int id, int businessId, int userId, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new InvalidOperationException("Reason is required.");

        await using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            var transfer = await _context.TransferInventories
                .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
            if (transfer is null) return false;

            var balance = await _context.BusinessFuelInventories
                .FirstOrDefaultAsync(x => x.Id == transfer.BusinessFuelInventoryId && !x.IsDeleted);
            if (balance is null || balance.BusinessId != businessId) return false;

            var before = new TransferSnapshot(transfer.ToStationId, transfer.Liters, transfer.Date, transfer.Note);
            var now = DateTime.UtcNow;
            balance.Liters += transfer.Liters;
            balance.UpdatedAt = now;

            transfer.IsDeleted = true;
            transfer.UpdatedAt = now;

            _context.TransferInventoryAudits.Add(new TransferInventoryAudit
            {
                TransferInventoryId = transfer.Id,
                Action = "Deleted",
                ChangedAt = now,
                ChangedByUserId = userId,
                Reason = reason.Trim(),
                ToStationId = before.ToStationId,
                Liters = before.Liters,
                Date = before.Date,
                BusinessId = businessId,
                CreatedAt = now,
                UpdatedAt = now,
                IsDeleted = false,
            });

            await _context.SaveChangesAsync();
            await tx.CommitAsync();
            return true;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task<List<TransferInventoryAuditDto>> GetTransferAuditAsync(int transferId, int businessId)
    {
        var ok = await _context.TransferInventories.AsNoTracking()
            .AnyAsync(t => t.Id == transferId && !t.IsDeleted &&
                          _context.BusinessFuelInventories.Any(b => b.Id == t.BusinessFuelInventoryId && !b.IsDeleted && b.BusinessId == businessId));
        if (!ok) return [];

        return await (
            from a in _context.TransferInventoryAudits.AsNoTracking()
            join u in _context.Users.AsNoTracking() on a.ChangedByUserId equals u.Id into uj
            from u in uj.DefaultIfEmpty()
            where !a.IsDeleted && a.TransferInventoryId == transferId
            orderby a.ChangedAt, a.Id
            select new TransferInventoryAuditDto
            {
                Id = a.Id,
                TransferInventoryId = a.TransferInventoryId,
                Action = a.Action,
                ChangedAt = a.ChangedAt,
                ChangedByUserId = a.ChangedByUserId,
                ChangedByName = u != null ? u.Name : null,
                Reason = a.Reason,
                ToStationId = a.ToStationId,
                Liters = a.Liters,
                Date = a.Date,
                BusinessId = a.BusinessId,
            }).ToListAsync();
    }

    public async Task<List<TransferPendingConfirmDto>> GetPendingTransfersForConfirmAsync(
        int businessId,
        int toStationId,
        int fuelTypeId)
    {
        return await (
            from t in _context.TransferInventories.AsNoTracking()
            join b in _context.BusinessFuelInventories.AsNoTracking() on t.BusinessFuelInventoryId equals b.Id
            join f in _context.FuelTypes.AsNoTracking() on b.FuelTypeId equals f.Id
            join s in _context.Stations.AsNoTracking() on t.ToStationId equals s.Id
            where !t.IsDeleted && !b.IsDeleted && !f.IsDeleted && !s.IsDeleted
                  && b.BusinessId == businessId
                  && t.ToStationId == toStationId
                  && b.FuelTypeId == fuelTypeId
                  && t.Status == TransferInventoryStatus.Pending
            orderby t.Date descending, t.Id descending
            select new TransferPendingConfirmDto
            {
                Id = t.Id,
                Liters = t.Liters,
                Date = t.Date,
                FuelName = f.FuelName,
                StationName = s.Name,
            }).ToListAsync();
    }

    public async Task<string?> TryMarkTransferReceivedAsync(
        int transferId,
        int businessId,
        int fuelTypeId,
        int toStationId,
        double liters,
        int userId)
    {
        if (liters <= 0) return "Liters must be greater than zero.";

        var transfer = await _context.TransferInventories
            .FirstOrDefaultAsync(x => x.Id == transferId && !x.IsDeleted);
        if (transfer is null) return "Transfer not found.";
        if (transfer.Status != TransferInventoryStatus.Pending) return "This transfer is not awaiting receipt.";

        var balance = await _context.BusinessFuelInventories
            .FirstOrDefaultAsync(x => x.Id == transfer.BusinessFuelInventoryId && !x.IsDeleted);
        if (balance is null || balance.BusinessId != businessId) return "Transfer does not belong to this business.";
        if (balance.FuelTypeId != fuelTypeId) return "Fuel type does not match the selected transfer.";
        if (transfer.ToStationId != toStationId) return "Destination station does not match the transfer.";
        if (!LitersMatch(transfer.Liters, liters)) return "Recorded liters must match the pending pool transfer.";

        var now = DateTime.UtcNow;
        transfer.Status = TransferInventoryStatus.Received;
        transfer.UpdatedAt = now;

        _context.TransferInventoryAudits.Add(new TransferInventoryAudit
        {
            TransferInventoryId = transfer.Id,
            Action = "Received",
            ChangedAt = now,
            ChangedByUserId = userId,
            Reason = null,
            ToStationId = transfer.ToStationId,
            Liters = transfer.Liters,
            Date = transfer.Date,
            BusinessId = businessId,
            CreatedAt = now,
            UpdatedAt = now,
            IsDeleted = false,
        });

        var fuelName = await _context.FuelTypes.AsNoTracking()
            .Where(x => x.Id == fuelTypeId)
            .Select(x => x.FuelName)
            .FirstOrDefaultAsync() ?? "Fuel";

        var userName = await _context.Users.AsNoTracking()
            .Where(x => x.Id == userId)
            .Select(x => x.Name)
            .FirstOrDefaultAsync() ?? "User";

        _context.AppNotifications.Add(new AppNotification
        {
            BusinessId = businessId,
            StationId = toStationId,
            Title = "Pool transfer marked received",
            Body = $"{userName} confirmed receipt of {transfer.Liters:0.###} L ({fuelName}) for this station.",
            TransferInventoryId = transfer.Id,
            ConfirmedByUserId = userId,
            CreatedAt = now,
            UpdatedAt = now,
            IsDeleted = false,
        });

        await _context.SaveChangesAsync();
        return null;
    }

    public async Task<string?> TryAutoCompleteMatchingPendingTransferForLiterInAsync(
        int businessId,
        int fuelTypeId,
        int receivingStationId,
        double liters,
        int userId)
    {
        if (liters <= 0) return null;

        var transferId = await (
            from t in _context.TransferInventories.AsNoTracking()
            join b in _context.BusinessFuelInventories.AsNoTracking() on t.BusinessFuelInventoryId equals b.Id
            where !t.IsDeleted && !b.IsDeleted
                  && b.BusinessId == businessId
                  && b.FuelTypeId == fuelTypeId
                  && t.ToStationId == receivingStationId
                  && t.Status == TransferInventoryStatus.Pending
                  && Math.Abs(t.Liters - liters) < 0.000001
            orderby t.Date, t.Id
            select t.Id).FirstOrDefaultAsync();

        if (transferId == 0) return null;

        return await TryMarkTransferReceivedAsync(transferId, businessId, fuelTypeId, receivingStationId, liters, userId);
    }

    public async Task<PagedResult<TransferInventoryAuditListRowDto>> GetTransferAuditsPagedForBusinessAsync(
        int businessId,
        int page,
        int pageSize,
        string? q)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query =
            from a in _context.TransferInventoryAudits.AsNoTracking()
            join t in _context.TransferInventories.AsNoTracking() on a.TransferInventoryId equals t.Id
            join b in _context.BusinessFuelInventories.AsNoTracking() on t.BusinessFuelInventoryId equals b.Id
            join f in _context.FuelTypes.AsNoTracking() on b.FuelTypeId equals f.Id
            join s in _context.Stations.AsNoTracking() on a.ToStationId equals s.Id
            join u in _context.Users.AsNoTracking() on a.ChangedByUserId equals u.Id into uj
            from u in uj.DefaultIfEmpty()
            where !a.IsDeleted && !t.IsDeleted && !b.IsDeleted && !f.IsDeleted && !s.IsDeleted && a.BusinessId == businessId
            orderby a.ChangedAt descending, a.Id descending
            select new { a, f.FuelName, s.Name, ChangedByName = u != null ? u.Name : null };

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(x =>
                EF.Functions.Like(x.a.Action, $"%{term}%") ||
                (x.a.Reason != null && EF.Functions.Like(x.a.Reason, $"%{term}%")) ||
                EF.Functions.Like(x.FuelName, $"%{term}%") ||
                EF.Functions.Like(x.Name, $"%{term}%") ||
                (x.ChangedByName != null && EF.Functions.Like(x.ChangedByName, $"%{term}%")));
        }

        var total = await query.CountAsync();
        var slice = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var items = slice.Select(x => new TransferInventoryAuditListRowDto
        {
            Id = x.a.Id,
            TransferInventoryId = x.a.TransferInventoryId,
            Action = x.a.Action,
            ChangedAt = x.a.ChangedAt,
            ChangedByUserId = x.a.ChangedByUserId,
            ChangedByName = x.ChangedByName,
            ToStationId = x.a.ToStationId,
            Liters = x.a.Liters,
            Date = x.a.Date,
            Reason = x.a.Reason,
            BusinessId = x.a.BusinessId,
            FuelName = x.FuelName,
            StationName = x.Name,
        }).ToList();

        return new PagedResult<TransferInventoryAuditListRowDto>(items, total, page, pageSize);
    }

    private async Task<TransferInventoryDto?> BuildTransferDtoAsync(int transferId, int businessId)
    {
        return await (
            from t in _context.TransferInventories.AsNoTracking()
            join b in _context.BusinessFuelInventories.AsNoTracking() on t.BusinessFuelInventoryId equals b.Id
            join f in _context.FuelTypes.AsNoTracking() on b.FuelTypeId equals f.Id
            join s in _context.Stations.AsNoTracking() on t.ToStationId equals s.Id
            join u in _context.Users.AsNoTracking() on t.CreatorId equals u.Id into uj
            from u in uj.DefaultIfEmpty()
            where t.Id == transferId && !t.IsDeleted && b.BusinessId == businessId
            select new TransferInventoryDto
            {
                Id = t.Id,
                BusinessFuelInventoryId = t.BusinessFuelInventoryId,
                BusinessId = b.BusinessId,
                FuelTypeId = b.FuelTypeId,
                FuelName = f.FuelName,
                ToStationId = t.ToStationId,
                StationName = s.Name,
                Liters = t.Liters,
                Date = t.Date,
                CreatorId = t.CreatorId,
                CreatorName = u != null ? u.Name : null,
                Note = t.Note,
                Status = t.Status,
            }).FirstOrDefaultAsync();
    }
}
