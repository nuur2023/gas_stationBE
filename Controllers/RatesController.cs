using System.Globalization;
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
public class RatesController(IRateRepository repository) : ControllerBase
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

    private bool ResolveRateBusiness(RateWriteRequestViewModel dto, out int targetBusinessId, out IActionResult? err)
    {
        targetBusinessId = 0;
        err = null;
        if (IsSuperAdmin(User))
        {
            if (dto.BusinessId <= 0)
            {
                err = BadRequest("Select a business for this rate.");
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

    [HttpGet]
    public async Task<IActionResult> GetPaged([FromQuery] int page = 1, [FromQuery] int pageSize = 50, [FromQuery] string? q = null)
    {
        if (IsSuperAdmin(User))
        {
            return Ok(await repository.GetPagedAsync(page, pageSize, q, null));
        }

        if (!TryGetJwtBusiness(out var bid))
        {
            return BadRequest("No business assigned to this user.");
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

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] RateWriteRequestViewModel dto)
    {
        if (!TryGetUserId(out var userId, out var uerr))
        {
            return uerr!;
        }

        if (!ResolveRateBusiness(dto, out var targetBusinessId, out var bizErr))
        {
            return bizErr!;
        }

        if (!double.TryParse(dto.RateNumber.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var rateNum))
        {
            return BadRequest("Invalid rate number.");
        }

        var entity = new Rate
        {
            Date = DateTime.UtcNow,
            RateNumber = rateNum,
            BusinessId = targetBusinessId,
            UsersId = userId,
            Active = dto.Active,
        };

        return Ok(await repository.AddAsync(entity));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] RateWriteRequestViewModel dto)
    {
        if (!TryGetUserId(out var userId, out var uerr))
        {
            return uerr!;
        }

        if (!ResolveRateBusiness(dto, out var targetBusinessId, out var bizErr))
        {
            return bizErr!;
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
                return BadRequest("Rate belongs to a different business.");
            }
        }
        else if (!TryGetJwtBusiness(out var bid) || existing.BusinessId != bid)
        {
            return Forbid();
        }

        if (!double.TryParse(dto.RateNumber.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var rateNum))
        {
            return BadRequest("Invalid rate number.");
        }

        existing.RateNumber = rateNum;
        existing.Active = dto.Active;
        existing.UsersId = userId;

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

        if (IsSuperAdmin(User))
        {
            return Ok(await repository.DeleteAsync(id));
        }

        if (!TryGetJwtBusiness(out var businessId))
        {
            return BadRequest("No business assigned to this user.");
        }

        if (existing.BusinessId != businessId)
        {
            return Forbid();
        }

        return Ok(await repository.DeleteAsync(id));
    }
}
