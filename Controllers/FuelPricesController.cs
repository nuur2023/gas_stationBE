using System.Security.Claims;
using backend.Common;
using backend.Data.Interfaces;
using backend.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace backend.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FuelPricesController(IFuelPriceRepository repository, IStationRepository stationRepository) : ControllerBase
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

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int? filterBusinessId = null, [FromQuery] int? filterStationId = null)
    {
        var rows = await repository.GetAllAsync();

        if (IsSuperAdmin(User))
        {
            if (filterBusinessId is > 0)
                rows = rows.Where(x => x.BusinessId == filterBusinessId.Value).ToList();
            if (filterStationId is > 0)
                rows = rows.Where(x => x.StationId == filterStationId.Value).ToList();
            return Ok(rows);
        }

        if (!TryGetJwtBusiness(out var bid))
            return BadRequest("No business assigned to this user.");

        var stationFilter = ListStationFilter.ForNonSuperAdmin(User, filterStationId);
        rows = rows.Where(x => x.BusinessId == bid).ToList();
        if (stationFilter is > 0)
            rows = rows.Where(x => x.StationId == stationFilter.Value).ToList();
        return Ok(rows);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var entity = await repository.GetByIdAsync(id);
        if (entity is null)
            return NotFound();

        if (IsSuperAdmin(User))
            return Ok(entity);

        if (!TryGetJwtBusiness(out var bid) || entity.BusinessId != bid)
            return NotFound();
        if (TryGetJwtStation(out var sid) && sid > 0 && entity.StationId != sid)
            return NotFound();
        return Ok(entity);
    }

    [HttpPost]
    public async Task<IActionResult> Create(FuelPrice model)
    {
        var station = await stationRepository.GetByIdAsync(model.StationId);
        if (station is null)
            return BadRequest("Station not found.");

        if (IsSuperAdmin(User))
        {
            if (model.BusinessId <= 0)
                return BadRequest("Business is required.");
            if (station.BusinessId != model.BusinessId)
                return BadRequest("Station does not belong to selected business.");
            return Ok(await repository.AddAsync(model));
        }

        if (!TryGetJwtBusiness(out var bid))
            return BadRequest("No business assigned to this user.");
        if (model.BusinessId > 0 && model.BusinessId != bid)
            return Forbid();
        if (station.BusinessId != bid)
            return Forbid();
        if (TryGetJwtStation(out var sid) && sid > 0 && model.StationId != sid)
            return BadRequest("You can only manage fuel prices for your assigned station.");

        model.BusinessId = bid;
        return Ok(await repository.AddAsync(model));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, FuelPrice model)
    {
        var existing = await repository.GetByIdAsync(id);
        if (existing is null)
            return NotFound();

        var station = await stationRepository.GetByIdAsync(model.StationId);
        if (station is null)
            return BadRequest("Station not found.");

        if (IsSuperAdmin(User))
        {
            if (model.BusinessId <= 0)
                return BadRequest("Business is required.");
            if (station.BusinessId != model.BusinessId || existing.BusinessId != model.BusinessId)
                return BadRequest("Fuel price belongs to a different business.");
            return Ok(await repository.UpdateAsync(id, model));
        }

        if (!TryGetJwtBusiness(out var bid))
            return BadRequest("No business assigned to this user.");
        if (existing.BusinessId != bid)
            return Forbid();
        if (model.BusinessId > 0 && model.BusinessId != bid)
            return Forbid();
        if (station.BusinessId != bid)
            return Forbid();
        if (TryGetJwtStation(out var sid) && sid > 0 && (existing.StationId != sid || model.StationId != sid))
            return BadRequest("You can only manage fuel prices for your assigned station.");

        model.BusinessId = bid;
        return Ok(await repository.UpdateAsync(id, model));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var existing = await repository.GetByIdAsync(id);
        if (existing is null)
            return NotFound();

        if (IsSuperAdmin(User))
            return Ok(await repository.DeleteAsync(id));

        if (!TryGetJwtBusiness(out var bid))
            return BadRequest("No business assigned to this user.");
        if (existing.BusinessId != bid)
            return Forbid();
        if (TryGetJwtStation(out var sid) && sid > 0 && existing.StationId != sid)
            return BadRequest("You can only manage fuel prices for your assigned station.");

        return Ok(await repository.DeleteAsync(id));
    }
}
