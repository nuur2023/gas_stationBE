using System.Globalization;
using System.Security.Claims;
using gas_station.Common;
using gas_station.Data.Interfaces;
using gas_station.Models;
using gas_station.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace gas_station.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DippingsController(
    IDippingRepository repository,
    IStationRepository stationRepository) : ControllerBase
{
    private const string SuperAdminRole = "SuperAdmin";
    private const string AdminRole = "Admin";

    private static bool IsSuperAdmin(ClaimsPrincipal user) =>
        user.IsInRole(SuperAdminRole);

    private static bool IsAdmin(ClaimsPrincipal user) =>
        user.IsInRole(AdminRole);

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

    [HttpGet]
    public async Task<IActionResult> GetPaged(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? q = null,
        [FromQuery] int? businessId = null,
        [FromQuery] int? filterStationId = null)
    {
        if (IsSuperAdmin(User))
        {
            return Ok(await repository.GetPagedAsync(page, pageSize, q, businessId, filterStationId));
        }

        if (!TryGetJwtBusiness(out var bid))
        {
            return BadRequest("No business assigned to this user.");
        }

        if (businessId.HasValue && businessId.Value != bid)
        {
            return Forbid();
        }

        var stationFilter = ListStationFilter.ForNonSuperAdmin(User, filterStationId);

        return Ok(await repository.GetPagedAsync(page, pageSize, q, bid, stationFilter));
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
            if (!TryGetJwtBusiness(out var businessId) || entity.BusinessId != businessId)
            {
                return NotFound();
            }
        }

        return Ok(entity);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] DippingWriteRequestViewModel dto)
    {
        if (!double.TryParse(dto.AmountLiter.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var amount))
        {
            return BadRequest("Invalid amount (liters).");
        }

        if (IsSuperAdmin(User))
        {
            if (!TryGetUserId(out var userId, out var err))
            {
                return err!;
            }

            if (dto.BusinessId <= 0)
            {
                return BadRequest("Select a business for this dipping.");
            }

            var station = await stationRepository.GetByIdAsync(dto.StationId);
            if (station is null || station.BusinessId != dto.BusinessId)
            {
                return BadRequest("Station does not belong to the selected business.");
            }

            var entity = new Dipping
            {
                Name = dto.Name.Trim(),
                FuelTypeId = dto.FuelTypeId,
                AmountLiter = amount,
                StationId = dto.StationId,
                BusinessId = dto.BusinessId,
                UserId = userId,
            };

            return Ok(await repository.AddAsync(entity));
        }

        if (!AuthClaims.TryGetUserAndBusiness(User, out var uid, out var businessId))
        {
            return BadRequest("No business assigned to this user.");
        }

        // Non-Admin users linked to a station in JWT are pinned to that station. Admins may use the
        // workspace station chosen in Settings (client sends that id; JWT may still hold another default).
        if (!IsAdmin(User) && TryGetJwtStation(out var jwtStationId) && jwtStationId > 0 &&
            dto.StationId != jwtStationId)
        {
            return BadRequest("You can only record dipping for your assigned station.");
        }

        var st = await stationRepository.GetByIdAsync(dto.StationId);
        if (st is null || st.BusinessId != businessId)
        {
            return BadRequest("Station does not belong to your business.");
        }

        var row = new Dipping
        {
            Name = dto.Name.Trim(),
            FuelTypeId = dto.FuelTypeId,
            AmountLiter = amount,
            StationId = dto.StationId,
            BusinessId = businessId,
            UserId = uid,
        };

        return Ok(await repository.AddAsync(row));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] DippingWriteRequestViewModel dto)
    {
        if (!double.TryParse(dto.AmountLiter.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var amount))
        {
            return BadRequest("Invalid amount (liters).");
        }

        var existing = await repository.GetByIdAsync(id);
        if (existing is null)
        {
            return NotFound();
        }

        if (IsSuperAdmin(User))
        {
            if (!TryGetUserId(out var userId, out var err))
            {
                return err!;
            }

            var station = await stationRepository.GetByIdAsync(dto.StationId);
            if (station is null || station.BusinessId != existing.BusinessId)
            {
                return BadRequest("Station does not belong to this dipping's business.");
            }

            existing.Name = dto.Name.Trim();
            existing.FuelTypeId = dto.FuelTypeId;
            existing.AmountLiter = amount;
            existing.StationId = dto.StationId;
            existing.UserId = userId;

            return Ok(await repository.UpdateAsync(id, existing));
        }

        if (!AuthClaims.TryGetUserAndBusiness(User, out var userId2, out var businessId))
        {
            return BadRequest("No business assigned to this user.");
        }

        if (existing.BusinessId != businessId)
        {
            return NotFound();
        }

        if (!IsAdmin(User) && TryGetJwtStation(out var jwtStationId) && jwtStationId > 0 &&
            dto.StationId != jwtStationId)
        {
            return BadRequest("You can only record dipping for your assigned station.");
        }

        var st2 = await stationRepository.GetByIdAsync(dto.StationId);
        if (st2 is null || st2.BusinessId != businessId)
        {
            return BadRequest("Station does not belong to your business.");
        }

        existing.Name = dto.Name.Trim();
        existing.FuelTypeId = dto.FuelTypeId;
        existing.AmountLiter = amount;
        existing.StationId = dto.StationId;
        existing.UserId = userId2;

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

        if (!AuthClaims.TryGetUserAndBusiness(User, out _, out var businessId))
        {
            return BadRequest("No business assigned to this user.");
        }

        if (existing.BusinessId != businessId)
        {
            return NotFound();
        }

        return Ok(await repository.DeleteAsync(id));
    }
}
