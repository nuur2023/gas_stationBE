using System.Security.Claims;
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
public class DippingPumpsController(
    GasStationDBContext dbContext,
    IDippingPumpRepository dippingPumpRepository,
    INozzleRepository nozzleRepository,
    IDippingRepository dippingRepository) : ControllerBase
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

    [HttpGet("by-business/{businessId:int}")]
    public async Task<IActionResult> ListByBusiness(int businessId)
    {
        if (businessId <= 0) return BadRequest("businessId is required.");
        if (!IsSuperAdmin(User))
        {
            if (!TryGetJwtBusiness(out var bid) || bid != businessId) return Forbid();
        }

        var rows = await dbContext.Set<DippingPump>()
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.BusinessId == businessId)
            .OrderBy(x => x.StationId).ThenBy(x => x.NozzleId).ThenBy(x => x.Id)
            .ToListAsync();
        return Ok(rows);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] DippingPumpWriteRequestViewModel dto)
    {
        if (!TryGetUserId(out var userId, out var uerr))
            return uerr!;

        int targetBusinessId;
        if (IsSuperAdmin(User))
        {
            if (dto.BusinessId <= 0) return BadRequest("businessId is required.");
            targetBusinessId = dto.BusinessId;
        }
        else
        {
            if (!TryGetJwtBusiness(out var bid)) return BadRequest("No business assigned to this user.");
            if (dto.BusinessId > 0 && dto.BusinessId != bid) return Forbid();
            targetBusinessId = bid;
        }

        var nozzle = await nozzleRepository.GetByIdAsync(dto.NozzleId);
        if (nozzle is null || nozzle.BusinessId != targetBusinessId || nozzle.StationId != dto.StationId)
            return BadRequest("Nozzle does not match selected business/station.");

        var dip = await dippingRepository.GetByIdAsync(dto.DippingId);
        if (dip is null || dip.BusinessId != targetBusinessId || dip.StationId != dto.StationId)
            return BadRequest("Dipping does not match selected business/station.");

        var existing = await dippingPumpRepository.GetFirstByNozzleIdAsync(dto.NozzleId);
        if (existing is not null)
        {
            existing.DippingId = dto.DippingId;
            existing.StationId = dto.StationId;
            existing.BusinessId = targetBusinessId;
            existing.UserId = userId;
            return Ok(await dippingPumpRepository.UpdateAsync(existing.Id, existing));
        }

        var added = await dippingPumpRepository.AddAsync(new DippingPump
        {
            NozzleId = dto.NozzleId,
            DippingId = dto.DippingId,
            StationId = dto.StationId,
            BusinessId = targetBusinessId,
            UserId = userId,
        });
        return Ok(added);
    }
}
