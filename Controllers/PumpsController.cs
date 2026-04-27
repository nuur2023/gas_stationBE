using System.Security.Claims;
using backend.Common;
using backend.Data.Interfaces;
using backend.Models;
using backend.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace backend.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PumpsController(
    IPumpRepository repository,
    IStationRepository stationRepository) : ControllerBase
{
    private const string SuperAdminRole = "SuperAdmin";

    private static bool IsSuperAdmin(ClaimsPrincipal user) =>
        user.IsInRole(SuperAdminRole);

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

    private bool ResolvePumpBusiness(PumpWriteRequestViewModel dto, out int targetBusinessId, out IActionResult? error)
    {
        targetBusinessId = 0;
        error = null;

        if (IsSuperAdmin(User))
        {
            if (dto.BusinessId <= 0)
            {
                error = BadRequest("Select a business for this pump.");
                return false;
            }

            targetBusinessId = dto.BusinessId;
            return true;
        }

        if (!TryGetJwtBusiness(out var jwtBid))
        {
            error = BadRequest("No business assigned to this user.");
            return false;
        }

        if (dto.BusinessId > 0 && dto.BusinessId != jwtBid)
        {
            error = Forbid();
            return false;
        }

        targetBusinessId = jwtBid;
        return true;
    }

    private async Task<IActionResult?> ValidateStationAsync(
        int targetBusinessId,
        int stationId)
    {
        var station = await stationRepository.GetByIdAsync(stationId);
        if (station is null || station.BusinessId != targetBusinessId)
        {
            return BadRequest("Station does not belong to the selected business.");
        }

        return null;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? q,
        [FromQuery] int? dippingId,
        [FromQuery] int? stationId,
        [FromQuery] int? businessId,
        [FromQuery] int? filterBusinessId,
        [FromQuery] int? filterStationId = null)
    {
        var wantsPaged = page.HasValue || pageSize.HasValue || !string.IsNullOrWhiteSpace(q);
        if (wantsPaged)
        {
            int? pagedBiz = null;
            if (IsSuperAdmin(User))
            {
                pagedBiz = filterBusinessId;
                return Ok(await repository.GetPagedAsync(page ?? 1, pageSize ?? 50, q, pagedBiz, filterStationId));
            }

            if (!TryGetJwtBusiness(out var bid))
            {
                return BadRequest("No business assigned to this user.");
            }

            if (filterBusinessId.HasValue && filterBusinessId.Value != bid)
            {
                return Forbid();
            }

            var stationFilter = ListStationFilter.ForNonSuperAdmin(User, filterStationId);
            return Ok(await repository.GetPagedAsync(page ?? 1, pageSize ?? 50, q, bid, stationFilter));
        }

        if (dippingId.HasValue || stationId.HasValue || businessId.HasValue)
        {
            return Ok(await repository.GetFilteredAsync(dippingId, stationId, businessId));
        }

        return Ok(await repository.GetAllAsync());
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
    public async Task<IActionResult> Create([FromBody] PumpWriteRequestViewModel dto)
    {
        if (!TryGetUserId(out var userId, out var uerr))
        {
            return uerr!;
        }

        if (!ResolvePumpBusiness(dto, out var targetBusinessId, out var bizErr))
        {
            return bizErr!;
        }

        var bad = await ValidateStationAsync(targetBusinessId, dto.StationId);
        if (bad is not null)
        {
            return bad;
        }

        var pump = new Pump
        {
            PumpNumber = dto.PumpNumber.Trim(),
            StationId = dto.StationId,
            BusinessId = targetBusinessId,
            UserId = userId,
        };

        return Ok(await repository.AddAsync(pump));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] PumpWriteRequestViewModel dto)
    {
        if (!TryGetUserId(out var userId, out var uerr))
        {
            return uerr!;
        }

        TryGetJwtBusiness(out var jwtBusinessId);

        if (!ResolvePumpBusiness(dto, out var targetBusinessId, out var bizErr))
        {
            return bizErr!;
        }

        var existing = await repository.GetByIdAsync(id);
        if (existing is null)
        {
            return NotFound();
        }

        if (!IsSuperAdmin(User))
        {
            if (!TryGetJwtBusiness(out jwtBusinessId) || existing.BusinessId != jwtBusinessId)
            {
                return NotFound();
            }

            targetBusinessId = jwtBusinessId;
        }

        var bad = await ValidateStationAsync(targetBusinessId, dto.StationId);
        if (bad is not null)
        {
            return bad;
        }

        existing.PumpNumber = dto.PumpNumber.Trim();
        existing.StationId = dto.StationId;
        existing.BusinessId = targetBusinessId;
        existing.UserId = userId;

        return Ok(await repository.UpdateAsync(id, existing));
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
            if (!TryGetJwtBusiness(out var bid) || existing.BusinessId != bid)
            {
                return NotFound();
            }
        }

        return Ok(await repository.DeleteAsync(id));
    }
}
