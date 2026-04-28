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
public class GeneratorUsagesController(
    IGeneratorUsageRepository repository,
    IStationRepository stationRepository,
    IFuelTypeRepository fuelTypeRepository,
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

    private bool ResolveGeneratorBusiness(GeneratorUsageWriteRequestViewModel dto, out int targetBusinessId, out IActionResult? err)
    {
        targetBusinessId = 0;
        err = null;
        if (IsSuperAdmin(User))
        {
            if (dto.BusinessId <= 0)
            {
                err = BadRequest("Select a business for this generator usage.");
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

    private async Task<IActionResult?> ValidateGeneratorStationAsync(int targetBusinessId, int stationId)
    {
        var st = await stationRepository.GetByIdAsync(stationId);
        if (st is null || st.BusinessId != targetBusinessId)
        {
            return BadRequest("Station does not belong to the selected business.");
        }

        if (TryGetJwtStation(out var js) && js > 0 && stationId != js)
        {
            return BadRequest("You can only record generator usage for your assigned station.");
        }

        return null;
    }

    private async Task<IActionResult?> ValidateGeneratorFuelTypeAsync(int targetBusinessId, int fuelTypeId)
    {
        if (fuelTypeId <= 0)
        {
            return BadRequest("Select a fuel type.");
        }

        var ft = await fuelTypeRepository.GetByIdAsync(fuelTypeId);
        if (ft is null || ft.BusinessId != targetBusinessId)
        {
            return BadRequest("Fuel type does not belong to the selected business.");
        }

        return null;
    }

    [HttpGet]
    public async Task<IActionResult> GetPaged(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? q = null,
        [FromQuery] int? filterStationId = null)
    {
        if (IsSuperAdmin(User))
        {
            return Ok(await repository.GetPagedAsync(page, pageSize, q, null, filterStationId));
        }

        if (!TryGetJwtBusiness(out var bid))
        {
            return BadRequest("No business assigned to this user.");
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
            if (!TryGetJwtBusiness(out var bid) || entity.BusinessId != bid)
            {
                return NotFound();
            }
        }

        return Ok(entity);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] GeneratorUsageWriteRequestViewModel dto)
    {
        if (!TryGetUserId(out var userId, out var uerr))
        {
            return uerr!;
        }

        if (!ResolveGeneratorBusiness(dto, out var targetBusinessId, out var bizErr))
        {
            return bizErr!;
        }

        var bad = await ValidateGeneratorStationAsync(targetBusinessId, dto.StationId);
        if (bad is not null)
        {
            return bad;
        }

        var badFt = await ValidateGeneratorFuelTypeAsync(targetBusinessId, dto.FuelTypeId);
        if (badFt is not null)
        {
            return badFt;
        }

        if (!double.TryParse(dto.LtrUsage.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var liters))
        {
            return BadRequest("Invalid liters usage.");
        }
        if (liters <= 0)
        {
            return BadRequest("Liters usage must be greater than zero.");
        }

        var dipping = await dippingRepository.GetFirstByStationAndFuelAsync(dto.StationId, dto.FuelTypeId);
        if (dipping is null)
        {
            return BadRequest("No dipping tank found for the selected station and fuel type.");
        }
        if (dipping.AmountLiter < liters)
        {
            return BadRequest("Insufficient dipping liters for this generator usage.");
        }

        dipping.AmountLiter -= liters;
        await dippingRepository.UpdateAsync(dipping.Id, dipping);

        var entity = new GeneratorUsage
        {
            Date = dto.Date?.UtcDateTime ?? DateTime.UtcNow,
            LtrUsage = liters,
            UsersId = userId,
            BusinessId = targetBusinessId,
            StationId = dto.StationId,
            FuelTypeId = dto.FuelTypeId,
        };

        return Ok(await repository.AddAsync(entity));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] GeneratorUsageWriteRequestViewModel dto)
    {
        if (!TryGetUserId(out var userId, out var uerr))
        {
            return uerr!;
        }

        if (!ResolveGeneratorBusiness(dto, out var targetBusinessId, out var bizErr))
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
                return BadRequest("Record belongs to a different business.");
            }
        }
        else if (!TryGetJwtBusiness(out var bid) || existing.BusinessId != bid)
        {
            return Forbid();
        }

        var bad = await ValidateGeneratorStationAsync(targetBusinessId, dto.StationId);
        if (bad is not null)
        {
            return bad;
        }

        var badFt = await ValidateGeneratorFuelTypeAsync(targetBusinessId, dto.FuelTypeId);
        if (badFt is not null)
        {
            return badFt;
        }

        if (!double.TryParse(dto.LtrUsage.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var liters))
        {
            return BadRequest("Invalid liters usage.");
        }

        existing.LtrUsage = liters;
        existing.UsersId = userId;
        existing.StationId = dto.StationId;
        existing.FuelTypeId = dto.FuelTypeId;
        if (dto.Date.HasValue)
            existing.Date = dto.Date.Value.UtcDateTime;

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
