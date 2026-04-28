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
public class NozzlesController(
    INozzleRepository nozzleRepository,
    IDippingPumpRepository dippingPumpRepository,
    IPumpRepository pumpRepository,
    IStationRepository stationRepository) : ControllerBase
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

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] NozzleWriteRequestViewModel dto)
    {
        if (!TryGetUserId(out var userId, out var uerr))
            return uerr!;

        int targetBusinessId;
        if (IsSuperAdmin(User))
        {
            if (dto.BusinessId <= 0)
                return BadRequest("businessId is required.");
            targetBusinessId = dto.BusinessId;
        }
        else
        {
            if (!TryGetJwtBusiness(out var bid))
                return BadRequest("No business assigned to this user.");
            if (dto.BusinessId > 0 && dto.BusinessId != bid)
                return Forbid();
            targetBusinessId = bid;
        }

        if (dto.PumpId <= 0 || dto.StationId <= 0)
            return BadRequest("pumpId and stationId are required.");

        var pump = await pumpRepository.GetByIdAsync(dto.PumpId);
        if (pump is null || pump.BusinessId != targetBusinessId || pump.StationId != dto.StationId)
            return BadRequest("Pump does not match the selected business/station.");

        var station = await stationRepository.GetByIdAsync(dto.StationId);
        if (station is null || station.BusinessId != targetBusinessId)
            return BadRequest("Station does not belong to the selected business.");

        var name = string.IsNullOrWhiteSpace(dto.Name) ? $"Nozzle-{dto.PumpId}" : dto.Name.Trim();
        var added = await nozzleRepository.AddAsync(new Nozzle
        {
            Name = name,
            PumpId = dto.PumpId,
            StationId = dto.StationId,
            BusinessId = targetBusinessId,
            UserId = userId,
        });

        return Ok(added);
    }

    /// <summary>Nozzles for a station with pump number and primary dipping id (for inventory dropdowns).</summary>
    [HttpGet("by-station/{stationId:int}")]
    public async Task<IActionResult> ListByStation(int stationId, [FromQuery] int businessId)
    {
        if (businessId <= 0)
            return BadRequest("businessId is required.");

        if (!IsSuperAdmin(User))
        {
            if (!TryGetJwtBusiness(out var bid) || bid != businessId)
                return Forbid();
        }

        var nozzles = await nozzleRepository.ListByStationAsync(stationId, businessId);
        var rows = new List<NozzleStationRowDto>();
        foreach (var n in nozzles)
        {
            var dipId = await dippingPumpRepository.GetDippingIdByNozzleIdAsync(n.Id);
            var pump = await pumpRepository.GetByIdAsync(n.PumpId);
            if (pump is null)
                continue;
            rows.Add(new NozzleStationRowDto(n.Id, n.PumpId, pump.PumpNumber, n.Name, n.StationId, n.BusinessId, dipId ?? 0));
        }

        return Ok(rows);
    }

    /// <summary>Nozzles on a pump with linked dipping id (for pump edit UI).</summary>
    [HttpGet("for-pump/{pumpId:int}")]
    public async Task<IActionResult> ListForPump(int pumpId)
    {
        var pump = await pumpRepository.GetByIdAsync(pumpId);
        if (pump is null)
            return NotFound();

        if (!IsSuperAdmin(User))
        {
            if (!TryGetJwtBusiness(out var bid) || pump.BusinessId != bid)
                return NotFound();
        }

        var nozzles = await nozzleRepository.ListByPumpIdAsync(pumpId);
        var rows = new List<NozzleForPumpRowDto>();
        foreach (var n in nozzles)
        {
            var dipId = await dippingPumpRepository.GetDippingIdByNozzleIdAsync(n.Id);
            rows.Add(new NozzleForPumpRowDto(n.Id, n.Name, dipId ?? 0));
        }

        return Ok(rows);
    }

    /// <summary>All nozzles for a business (for inventory tables / labels).</summary>
    [HttpGet("by-business/{businessId:int}")]
    public async Task<IActionResult> ListByBusiness(int businessId)
    {
        if (businessId <= 0)
            return BadRequest("businessId is required.");

        if (!IsSuperAdmin(User))
        {
            if (!TryGetJwtBusiness(out var bid) || bid != businessId)
                return Forbid();
        }

        var nozzles = await nozzleRepository.ListByBusinessAsync(businessId);
        var rows = new List<NozzleStationRowDto>();
        foreach (var n in nozzles)
        {
            var dipId = await dippingPumpRepository.GetDippingIdByNozzleIdAsync(n.Id);
            var pump = await pumpRepository.GetByIdAsync(n.PumpId);
            if (pump is null)
                continue;
            rows.Add(new NozzleStationRowDto(n.Id, n.PumpId, pump.PumpNumber, n.Name, n.StationId, n.BusinessId, dipId ?? 0));
        }

        return Ok(rows);
    }
}
