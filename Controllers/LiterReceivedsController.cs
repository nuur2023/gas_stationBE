using System.Globalization;
using System.Security.Claims;
using gas_station.Common;
using gas_station.Data.Context;
using gas_station.Data.Interfaces;
using gas_station.Models;
using gas_station.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace gas_station.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class LiterReceivedsController(
    ILiterReceivedRepository repository,
    IStationRepository stationRepository,
    IDippingRepository dippingRepository,
    IBusinessFuelInventoryLedgerRepository busFuelLedger,
    GasStationDBContext dbContext) : ControllerBase
{
    private const string SuperAdminRole = "SuperAdmin";

    private static bool IsSuperAdmin(ClaimsPrincipal user) => user.IsInRole(SuperAdminRole);

    private static bool IsInFlow(string type) =>
        string.Equals(type, "In", StringComparison.OrdinalIgnoreCase);

    private static bool IsOutFlow(string type) =>
        string.Equals(type, "Out", StringComparison.OrdinalIgnoreCase);

    private static double DippingDeltaFor(string type, double liters)
    {
        if (IsInFlow(type)) return liters;
        if (IsOutFlow(type)) return -liters;
        return 0;
    }

    private bool TryGetJwtBusiness(out int businessId)
    {
        businessId = 0;
        var bidStr = User.FindFirstValue("business_id");
        return !string.IsNullOrEmpty(bidStr) && int.TryParse(bidStr, out businessId);
    }

    private bool TryGetJwtStation(out int stationId)
    {
        stationId = 0;
        var s = User.FindFirstValue("station_id");
        return !string.IsNullOrEmpty(s) && int.TryParse(s, out stationId);
    }

    private bool TryGetUserId(out int userId, out IActionResult? error)
    {
        userId = 0;
        error = null;
        var uid = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(uid) || !int.TryParse(uid, out userId))
        {
            error = Unauthorized();
            return false;
        }

        return true;
    }

    private bool ResolveLiterBusiness(LiterReceivedWriteRequestViewModel dto, out int targetBusinessId, out IActionResult? err)
    {
        targetBusinessId = 0;
        err = null;
        if (IsSuperAdmin(User))
        {
            if (dto.BusinessId <= 0)
            {
                err = BadRequest("Select a business for this liter received entry.");
                return false;
            }

            targetBusinessId = dto.BusinessId;
            return true;
        }

        if (!TryGetJwtBusiness(out var bid))
        {
            err = BadRequest("No business assigned to this user.");
            return false;
        }

        if (dto.BusinessId > 0 && dto.BusinessId != bid)
        {
            err = Forbid();
            return false;
        }

        targetBusinessId = bid;
        return true;
    }

    private async Task<IActionResult?> ValidateStationBelongsToBusinessAsync(int targetBusinessId, int stationId)
    {
        var st = await stationRepository.GetByIdAsync(stationId);
        if (st is null || st.BusinessId != targetBusinessId)
        {
            return BadRequest("Station does not belong to the selected business.");
        }

        return null;
    }

    /// <summary>Optional In-only: origin station must belong to the business and differ from the receiving station.</summary>
    private async Task<IActionResult?> ValidateOptionalInFromStationAsync(
        int targetBusinessId,
        int receivingStationId,
        int? fromStationId)
    {
        if (fromStationId is null or <= 0) return null;
        if (fromStationId.Value == receivingStationId)
        {
            return BadRequest("From station cannot be the same as the receiving station.");
        }

        return await ValidateStationBelongsToBusinessAsync(targetBusinessId, fromStationId.Value);
    }

    private async Task<IActionResult?> TryAdjustDippingAsync(int stationId, int fuelTypeId, double delta)
    {
        if (delta == 0) return null;

        var dipping = await dippingRepository.GetFirstByStationAndFuelAsync(stationId, fuelTypeId);
        if (dipping is null)
        {
            return BadRequest("No dipping record exists for this station and fuel type. Create a dipping entry first.");
        }

        var next = dipping.AmountLiter + delta;
        if (next < 0)
        {
            return BadRequest("Dipping balance cannot go negative for this operation.");
        }

        dipping.AmountLiter = next;
        await dippingRepository.UpdateAsync(dipping.Id, dipping);
        return null;
    }

    /// <summary>Reverses the dipping effect of a saved liter row (for update/delete).</summary>
    private Task<IActionResult?> RevertDippingForLiterAsync(LiterReceived row) =>
        TryAdjustDippingAsync(row.StationId, row.FuelTypeId, -DippingDeltaFor(row.Type, row.ReceivedLiter));

    /// <summary>Applies dipping change for a new or updated liter row.</summary>
    private Task<IActionResult?> ApplyDippingForLiterAsync(LiterReceived row) =>
        TryAdjustDippingAsync(row.StationId, row.FuelTypeId, DippingDeltaFor(row.Type, row.ReceivedLiter));

    private async Task<IActionResult?> TryConfirmPoolTransferReceiptAsync(
        string normalizedFlow,
        int targetBusinessId,
        LiterReceivedWriteRequestViewModel dto,
        int userId,
        int? resolvedToStationId,
        double liters)
    {
        if (!dto.ConfirmBusinessPoolTransferReceived)
            return null;

        if (!IsOutFlow(normalizedFlow))
            return BadRequest("Business pool transfer confirmation applies only to Out (transfer) records.");

        if (dto.ConfirmTransferInventoryId is null or <= 0)
            return BadRequest("Select a pending pool transfer when confirming receipt.");

        if (resolvedToStationId is null or <= 0)
            return BadRequest("Destination station is required to confirm a pool transfer.");

        var msg = await busFuelLedger.TryMarkTransferReceivedAsync(
            dto.ConfirmTransferInventoryId.Value,
            targetBusinessId,
            dto.FuelTypeId,
            resolvedToStationId.Value,
            liters,
            userId);
        return msg is null ? null : BadRequest(msg);
    }

    private async Task<(IActionResult? Error, int StationId, int? ToStationId)> ResolveStationsForCreateOrUpdateAsync(
        LiterReceivedWriteRequestViewModel dto,
        int targetBusinessId)
    {
        var flow = dto.Type.Trim();

        if (!IsInFlow(flow) && !IsOutFlow(flow))
        {
            return (BadRequest("Type must be In or Out."), 0, null);
        }

        if (IsInFlow(flow))
        {
            int stationId;
            if (!IsSuperAdmin(User) && TryGetJwtStation(out var jwtSt) && jwtSt > 0)
            {
                stationId = jwtSt;
                var bad = await ValidateStationBelongsToBusinessAsync(targetBusinessId, stationId);
                if (bad is not null) return (bad, 0, null);
            }
            else if (IsSuperAdmin(User))
            {
                if (dto.StationId <= 0)
                {
                    return (BadRequest("Select the receiving station for In."), 0, null);
                }

                stationId = dto.StationId;
                var bad = await ValidateStationBelongsToBusinessAsync(targetBusinessId, stationId);
                if (bad is not null) return (bad, 0, null);
            }
            else
            {
                if (dto.StationId <= 0)
                {
                    return (BadRequest("No station assigned. Select a station or contact an administrator."), 0, null);
                }

                stationId = dto.StationId;
                var bad = await ValidateStationBelongsToBusinessAsync(targetBusinessId, stationId);
                if (bad is not null) return (bad, 0, null);

                if (TryGetJwtStation(out var jwtSt2) && jwtSt2 > 0 && stationId != jwtSt2)
                {
                    return (BadRequest("You can only record liters for your assigned station."), 0, null);
                }
            }

            var fromErr = await ValidateOptionalInFromStationAsync(targetBusinessId, stationId, dto.FromStationId);
            if (fromErr is not null) return (fromErr, 0, null);

            return (null, stationId, null);
        }

        // Out
        int outStationId;
        if (!IsSuperAdmin(User) && TryGetJwtStation(out var fromSt) && fromSt > 0)
        {
            outStationId = fromSt;
        }
        else if (IsSuperAdmin(User))
        {
            if (dto.StationId <= 0)
            {
                return (BadRequest("Select the station sending fuel (Out)."), 0, null);
            }

            outStationId = dto.StationId;
        }
        else
        {
            if (dto.StationId <= 0)
            {
                return (BadRequest("Select your station or assign a station to your account."), 0, null);
            }

            outStationId = dto.StationId;
        }

        var badFrom = await ValidateStationBelongsToBusinessAsync(targetBusinessId, outStationId);
        if (badFrom is not null) return (badFrom, 0, null);

        if (!IsSuperAdmin(User) && TryGetJwtStation(out var jwtFrom) && jwtFrom > 0 && outStationId != jwtFrom)
        {
            return (BadRequest("Out transfers must be recorded from your assigned station."), 0, null);
        }

        if (dto.ToStationId is null or <= 0)
        {
            return (BadRequest("Select the destination station for Out."), 0, null);
        }

        var dest = dto.ToStationId.Value;
        if (dest == outStationId)
        {
            return (BadRequest("Destination station must differ from the sending station."), 0, null);
        }

        var badTo = await ValidateStationBelongsToBusinessAsync(targetBusinessId, dest);
        if (badTo is not null) return (badTo, 0, null);

        return (null, outStationId, dest);
    }

    [HttpGet]
    public async Task<IActionResult> GetPaged(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? q = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int? filterStationId = null)
    {
        if (IsSuperAdmin(User))
        {
            return Ok(await repository.GetPagedAsync(page, pageSize, q, null, from, to, filterStationId));
        }

        if (!TryGetJwtBusiness(out var bid))
        {
            return BadRequest("No business assigned to this user.");
        }

        var stationFilter = ListStationFilter.ForNonSuperAdmin(User, filterStationId);
        return Ok(await repository.GetPagedAsync(page, pageSize, q, bid, from, to, stationFilter));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var entity = await repository.GetByIdAsync(id);
        if (entity is null)
        {
            return NotFound();
        }

        if (!IsSuperAdmin(User))
        {
            if (!TryGetJwtBusiness(out var bid) || entity.BusinessId != bid)
            {
                return NotFound();
            }
        }

        return Ok(entity);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] LiterReceivedWriteRequestViewModel dto)
    {
        if (!TryGetUserId(out var userId, out var uerr))
        {
            return uerr!;
        }

        if (!ResolveLiterBusiness(dto, out var targetBusinessId, out var bizErr))
        {
            return bizErr!;
        }

        if (!double.TryParse(dto.ReceivedLiter.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var liters))
        {
            return BadRequest("Invalid received liters.");
        }

        var (stationErr, stationId, toStationId) = await ResolveStationsForCreateOrUpdateAsync(dto, targetBusinessId);
        if (stationErr is not null)
        {
            return stationErr;
        }

        var flow = dto.Type.Trim();
        var normalizedFlow = IsInFlow(flow) ? "In" : "Out";
        var targo = dto.Targo.Trim();
        var driver = dto.DriverName.Trim();

        var entity = new LiterReceived
        {
            Type = normalizedFlow,
            Targo = targo,
            DriverName = driver,
            Name = driver,
            FuelTypeId = dto.FuelTypeId,
            ReceivedLiter = liters,
            StationId = stationId,
            ToStationId = normalizedFlow == "Out" ? toStationId : null,
            FromStationId =
                normalizedFlow == "In" && dto.FromStationId.HasValue && dto.FromStationId.Value > 0
                    ? dto.FromStationId
                    : null,
            BusinessId = targetBusinessId,
            UserId = userId,
            Date = dto.RecordedAt?.UtcDateTime ?? DateTime.UtcNow,
        };

        await using var tx = await dbContext.Database.BeginTransactionAsync();
        try
        {
            await repository.AddAsync(entity);
            var dipErr = await ApplyDippingForLiterAsync(entity);
            if (dipErr is not null)
            {
                await tx.RollbackAsync();
                return dipErr;
            }

            var poolErr = await TryConfirmPoolTransferReceiptAsync(
                normalizedFlow,
                targetBusinessId,
                dto,
                userId,
                toStationId,
                liters);
            if (poolErr is not null)
            {
                await tx.RollbackAsync();
                return poolErr;
            }

            if (normalizedFlow == "In" && liters > 0)
            {
                var autoXferErr = await busFuelLedger.TryAutoCompleteMatchingPendingTransferForLiterInAsync(
                    targetBusinessId,
                    dto.FuelTypeId,
                    stationId,
                    liters,
                    userId);
                if (autoXferErr is not null)
                {
                    await tx.RollbackAsync();
                    return BadRequest(autoXferErr);
                }
            }

            await tx.CommitAsync();
            return Ok(entity);
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] LiterReceivedWriteRequestViewModel dto)
    {
        if (!TryGetUserId(out var userId, out var uerr))
        {
            return uerr!;
        }

        if (!ResolveLiterBusiness(dto, out var targetBusinessId, out var bizErr))
        {
            return bizErr!;
        }

        if (!double.TryParse(dto.ReceivedLiter.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var liters))
        {
            return BadRequest("Invalid received liters.");
        }

        var existing = await repository.GetByIdAsync(id);
        if (existing is null)
        {
            return NotFound();
        }

        if (IsSuperAdmin(User))
        {
            if (existing.BusinessId != targetBusinessId)
            {
                return BadRequest("Entry belongs to a different business.");
            }
        }
        else if (!TryGetJwtBusiness(out var bid) || existing.BusinessId != bid)
        {
            return NotFound();
        }

        var (stationErr2, stationId, toStationId) = await ResolveStationsForCreateOrUpdateAsync(dto, targetBusinessId);
        if (stationErr2 is not null)
        {
            return stationErr2;
        }

        var flow = dto.Type.Trim();
        var normalizedFlow = IsInFlow(flow) ? "In" : "Out";
        var targo = dto.Targo.Trim();
        var driver = dto.DriverName.Trim();

        await using var tx = await dbContext.Database.BeginTransactionAsync();
        try
        {
            var revertErr = await RevertDippingForLiterAsync(existing);
            if (revertErr is not null)
            {
                await tx.RollbackAsync();
                return revertErr;
            }

            existing.Type = normalizedFlow;
            existing.Targo = targo;
            existing.DriverName = driver;
            existing.Name = driver;
            existing.FuelTypeId = dto.FuelTypeId;
            existing.ReceivedLiter = liters;
            existing.StationId = stationId;
            existing.ToStationId = normalizedFlow == "Out" ? toStationId : null;
            existing.FromStationId =
                normalizedFlow == "In" && dto.FromStationId.HasValue && dto.FromStationId.Value > 0
                    ? dto.FromStationId
                    : null;
            existing.UserId = userId;
            if (dto.RecordedAt.HasValue)
                existing.Date = dto.RecordedAt.Value.UtcDateTime;

            await repository.UpdateAsync(id, existing);
            var applyErr = await ApplyDippingForLiterAsync(existing);
            if (applyErr is not null)
            {
                await tx.RollbackAsync();
                return applyErr;
            }

            var poolErr2 = await TryConfirmPoolTransferReceiptAsync(
                normalizedFlow,
                targetBusinessId,
                dto,
                userId,
                toStationId,
                liters);
            if (poolErr2 is not null)
            {
                await tx.RollbackAsync();
                return poolErr2;
            }

            if (normalizedFlow == "In" && liters > 0)
            {
                var autoXferErr2 = await busFuelLedger.TryAutoCompleteMatchingPendingTransferForLiterInAsync(
                    targetBusinessId,
                    dto.FuelTypeId,
                    stationId,
                    liters,
                    userId);
                if (autoXferErr2 is not null)
                {
                    await tx.RollbackAsync();
                    return BadRequest(autoXferErr2);
                }
            }

            await tx.CommitAsync();
            return Ok(existing);
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var existing = await repository.GetByIdAsync(id);
        if (existing is null)
        {
            return NotFound();
        }

        if (!IsSuperAdmin(User))
        {
            if (!TryGetJwtBusiness(out var businessId))
            {
                return BadRequest("No business assigned to this user.");
            }

            if (existing.BusinessId != businessId)
            {
                return NotFound();
            }
        }

        await using var tx = await dbContext.Database.BeginTransactionAsync();
        try
        {
            var revertErr = await RevertDippingForLiterAsync(existing);
            if (revertErr is not null)
            {
                await tx.RollbackAsync();
                return revertErr;
            }

            var deleted = await repository.DeleteAsync(id);
            await tx.CommitAsync();
            return Ok(deleted);
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }
}
