using System.Security.Claims;
using gas_station.Data.Interfaces;
using gas_station.Models;
using gas_station.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace gas_station.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class StationsController(IStationRepository repository) : ControllerBase
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

    /// <summary>SuperAdmin: optional query filters by business; omit to list all. Others: JWT business only.</summary>
    [HttpGet]
    public async Task<IActionResult> GetPaged(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? q = null,
        [FromQuery] int? businessId = null)
    {
        if (IsSuperAdmin(User))
        {
            return Ok(await repository.GetPagedAsync(page, pageSize, q, businessId));
        }

        if (!TryGetJwtBusiness(out var bid))
        {
            return BadRequest("No business assigned to this user.");
        }

        if (businessId.HasValue && businessId.Value != bid)
        {
            return Forbid();
        }

        return Ok(await repository.GetPagedAsync(page, pageSize, q, bid));
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

    private bool ResolveWriteBusiness(StationWriteRequestViewModel dto, out int targetBusinessId, out IActionResult? error)
    {
        targetBusinessId = 0;
        error = null;

        if (IsSuperAdmin(User))
        {
            if (dto.BusinessId <= 0)
            {
                error = BadRequest("Select a business for this station.");
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

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] StationWriteRequestViewModel dto)
    {
        if (!TryGetUserId(out var userId, out var uerr))
        {
            return uerr!;
        }

        if (!ResolveWriteBusiness(dto, out var targetBusinessId, out var err))
        {
            return err!;
        }

        var entity = new Station
        {
            Name = dto.Name.Trim(),
            Address = dto.Address.Trim(),
            IsActive = dto.IsActive,
            BusinessId = targetBusinessId,
            UserId = userId,
        };

        return Ok(await repository.AddAsync(entity));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] StationWriteRequestViewModel dto)
    {
        if (!TryGetUserId(out var userId, out var uerr))
        {
            return uerr!;
        }

        TryGetJwtBusiness(out var jwtBusinessId);

        if (!ResolveWriteBusiness(dto, out var targetBusinessId, out var err))
        {
            return err!;
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

        existing.Name = dto.Name.Trim();
        existing.Address = dto.Address.Trim();
        existing.IsActive = dto.IsActive;
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
