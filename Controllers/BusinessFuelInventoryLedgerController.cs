using System.Globalization;
using System.Security.Claims;
using gas_station.Data.Interfaces;
using gas_station.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace gas_station.Controllers;

[ApiController]
[Route("api/business-fuel-inventory")]
[Authorize]
public class BusinessFuelInventoryLedgerController(IBusinessFuelInventoryLedgerRepository repository) : ControllerBase
{
    private const string SuperAdminRole = "SuperAdmin";

    private static bool IsSuperAdmin(ClaimsPrincipal user) => user.IsInRole(SuperAdminRole);

    private bool TryGetJwtBusiness(out int businessId)
    {
        businessId = 0;
        var bidStr = User.FindFirstValue("business_id");
        return !string.IsNullOrEmpty(bidStr) && int.TryParse(bidStr, out businessId);
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

    private bool ResolveTargetBusiness(int dtoBusinessId, out int targetBusinessId, out IActionResult? err)
    {
        targetBusinessId = 0;
        err = null;
        if (IsSuperAdmin(User))
        {
            if (dtoBusinessId <= 0)
            {
                err = BadRequest("Select a business.");
                return false;
            }

            targetBusinessId = dtoBusinessId;
            return true;
        }

        if (!TryGetJwtBusiness(out var bid))
        {
            err = BadRequest("No business assigned to this user.");
            return false;
        }

        if (dtoBusinessId > 0 && dtoBusinessId != bid)
        {
            err = Forbid();
            return false;
        }

        targetBusinessId = bid;
        return true;
    }

    private bool ResolveQueryBusiness(int? queryBusinessId, out int targetBusinessId, out IActionResult? err)
    {
        targetBusinessId = 0;
        err = null;
        if (IsSuperAdmin(User))
        {
            if (queryBusinessId is null or <= 0)
            {
                err = BadRequest("Select a business (businessId query).");
                return false;
            }

            targetBusinessId = queryBusinessId.Value;
            return true;
        }

        if (!TryGetJwtBusiness(out var bid))
        {
            err = BadRequest("No business assigned to this user.");
            return false;
        }

        if (queryBusinessId is > 0 && queryBusinessId != bid)
        {
            err = Forbid();
            return false;
        }

        targetBusinessId = bid;
        return true;
    }

    [HttpGet("balances")]
    public async Task<IActionResult> GetBalances([FromQuery] int? businessId = null)
    {
        if (!ResolveQueryBusiness(businessId, out var bid, out var err))
            return err!;
        return Ok(await repository.GetBalancesAsync(bid));
    }

    [HttpGet("credits")]
    public async Task<IActionResult> GetCredits([FromQuery] int? businessId = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        if (!ResolveQueryBusiness(businessId, out var bid, out var err))
            return err!;
        return Ok(await repository.GetCreditsPagedAsync(bid, page, pageSize));
    }

    [HttpGet("transfers")]
    public async Task<IActionResult> GetTransfers([FromQuery] int? businessId = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        if (!ResolveQueryBusiness(businessId, out var bid, out var err))
            return err!;
        return Ok(await repository.GetTransfersPagedAsync(bid, page, pageSize));
    }

    [HttpPost("credits")]
    public async Task<IActionResult> PostCredit([FromBody] BusinessFuelInventoryCreditWriteRequest dto)
    {
        if (!ResolveTargetBusiness(dto.BusinessId, out var bid, out var bizErr))
            return bizErr!;
        if (!TryGetUserId(out var userId, out var uerr))
            return uerr!;

        if (!double.TryParse(dto.Liters.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var liters) || liters <= 0)
            return BadRequest("Invalid liters.");

        try
        {
            var created = await repository.CreditAsync(
                bid,
                dto.FuelTypeId,
                liters,
                dto.Date?.UtcDateTime ?? DateTime.UtcNow,
                userId,
                dto.Reference ?? string.Empty,
                dto.Note);
            return Ok(created);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpDelete("credits/{id:int}")]
    public async Task<IActionResult> DeleteCredit(int id, [FromBody] BusinessFuelInventoryCreditDeleteRequest body)
    {
        if (!ResolveTargetBusiness(body.BusinessId, out var targetBid, out var bizErr))
            return bizErr!;

        try
        {
            var ok = await repository.SoftDeleteCreditAsync(id, targetBid);
            return ok ? NoContent() : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("transfers")]
    public async Task<IActionResult> PostTransfer([FromBody] TransferInventoryWriteRequest dto)
    {
        if (!ResolveTargetBusiness(dto.BusinessId, out var bid, out var bizErr))
            return bizErr!;
        if (!TryGetUserId(out var userId, out var uerr))
            return uerr!;

        if (!double.TryParse(dto.Liters.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var liters) || liters <= 0)
            return BadRequest("Invalid liters.");

        try
        {
            var created = await repository.CreateTransferAsync(
                bid,
                dto.FuelTypeId,
                dto.ToStationId,
                liters,
                dto.Date?.UtcDateTime ?? DateTime.UtcNow,
                userId,
                dto.Note);
            return Ok(created);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPut("transfers/{id:int}")]
    public async Task<IActionResult> PutTransfer(int id, [FromBody] TransferInventoryUpdateRequest dto)
    {
        if (!ResolveTargetBusiness(dto.BusinessId, out var targetBid, out var bizErr))
            return bizErr!;

        if (!TryGetUserId(out var userId, out var uerr))
            return uerr!;

        if (!double.TryParse(dto.Liters.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var liters) || liters <= 0)
            return BadRequest("Invalid liters.");

        try
        {
            var updated = await repository.UpdateTransferAsync(
                id,
                targetBid,
                dto.ToStationId,
                liters,
                dto.Date?.UtcDateTime ?? DateTime.UtcNow,
                dto.Note,
                userId,
                dto.Reason);
            return updated is null ? NotFound() : Ok(updated);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpDelete("transfers/{id:int}")]
    public async Task<IActionResult> DeleteTransfer(int id, [FromBody] TransferInventoryDeleteRequest? body)
    {
        if (body is null)
            return BadRequest("Body is required.");
        if (!ResolveTargetBusiness(body.BusinessId, out var targetBid, out var bizErr))
            return bizErr!;
        if (!TryGetUserId(out var userId, out var uerr))
            return uerr!;

        var reason = body.Reason.Trim();
        if (string.IsNullOrEmpty(reason))
            return BadRequest("Reason is required.");

        try
        {
            var ok = await repository.SoftDeleteTransferAsync(id, targetBid, userId, reason);
            return ok ? NoContent() : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("transfers/audit-trail")]
    public async Task<IActionResult> GetTransferAuditTrail(
        [FromQuery] int? businessId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? q = null)
    {
        if (!ResolveQueryBusiness(businessId, out var bid, out var err))
            return err!;
        return Ok(await repository.GetTransferAuditsPagedForBusinessAsync(bid, page, pageSize, q));
    }

    [HttpGet("transfers/{id:int}/audit")]
    public async Task<IActionResult> GetTransferAudit(int id, [FromQuery] int? businessId = null)
    {
        if (!ResolveQueryBusiness(businessId, out var bid, out var err))
            return err!;
        return Ok(await repository.GetTransferAuditAsync(id, bid));
    }
}
