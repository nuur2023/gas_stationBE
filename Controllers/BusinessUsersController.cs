using gas_station.Data.Interfaces;
using gas_station.Models;
using gas_station.ViewModels;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace gas_station.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class BusinessUsersController(IBusinessUserRepository repository, IStationRepository stationRepository) : ControllerBase
{
    private const string SuperAdminRole = "SuperAdmin";
    private bool IsSuperAdmin() => User.IsInRole(SuperAdminRole);

    private bool TryGetJwtBusiness(out int businessId)
    {
        businessId = 0;
        var bid = User.FindFirstValue("business_id");
        return !string.IsNullOrEmpty(bid) && int.TryParse(bid, out businessId);
    }

    private int ResolveBusinessForWrite(int requestedBusinessId)
    {
        if (IsSuperAdmin()) return requestedBusinessId;
        if (!TryGetJwtBusiness(out var jwtBid) || jwtBid <= 0)
            throw new InvalidOperationException("No business assigned to this user.");
        return jwtBid;
    }

    [HttpGet]
    public async Task<IActionResult> GetPaged([FromQuery] int page = 1, [FromQuery] int pageSize = 50, [FromQuery] string? q = null)
    {
        int? businessFilter = null;
        if (!IsSuperAdmin())
        {
            if (!TryGetJwtBusiness(out var bid) || bid <= 0)
                return BadRequest("No business assigned to this user.");
            businessFilter = bid;
        }

        return Ok(await repository.GetPagedAsync(page, pageSize, q, businessFilter, includeElevatedRoles: IsSuperAdmin()));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var row = await repository.GetByIdAsync(id);
        if (row is null) return NotFound();
        if (!IsSuperAdmin())
        {
            if (!TryGetJwtBusiness(out var bid) || bid <= 0) return BadRequest("No business assigned to this user.");
            if (row.BusinessId != bid) return Forbid();
        }
        return Ok(row);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] BusinessUserWriteRequestViewModel dto)
    {
        int bid;
        try
        {
            bid = ResolveBusinessForWrite(dto.BusinessId);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }

        if (dto.StationId <= 0)
            return BadRequest("Station is required.");

        var station = await stationRepository.GetByIdAsync(dto.StationId);
        if (station is null || station.BusinessId != bid)
            return BadRequest("Station does not belong to the selected business.");

        if (await repository.LinkExistsAsync(dto.UserId, bid, dto.StationId))
            return BadRequest("This user is already linked to that station for this business.");

        var entity = new BusinessUser
        {
            UserId = dto.UserId,
            BusinessId = bid,
            StationId = dto.StationId,
        };

        return Ok(await repository.AddAsync(entity));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] BusinessUserWriteRequestViewModel dto)
    {
        var existing = await repository.GetByIdAsync(id);
        if (existing is null) return NotFound();
        if (!IsSuperAdmin())
        {
            if (!TryGetJwtBusiness(out var jwtBid) || jwtBid <= 0) return BadRequest("No business assigned to this user.");
            if (existing.BusinessId != jwtBid) return Forbid();
        }

        int bid;
        try
        {
            bid = ResolveBusinessForWrite(dto.BusinessId);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }

        if (dto.StationId <= 0)
            return BadRequest("Station is required.");

        var station = await stationRepository.GetByIdAsync(dto.StationId);
        if (station is null || station.BusinessId != bid)
            return BadRequest("Station does not belong to the selected business.");

        if (await repository.LinkExistsAsync(dto.UserId, bid, dto.StationId, excludeId: id))
            return BadRequest("This user is already linked to that station for this business.");

        var entity = new BusinessUser
        {
            UserId = dto.UserId,
            BusinessId = bid,
            StationId = dto.StationId,
        };

        return Ok(await repository.UpdateAsync(id, entity));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var existing = await repository.GetByIdAsync(id);
        if (existing is null) return NotFound();
        if (!IsSuperAdmin())
        {
            if (!TryGetJwtBusiness(out var bid) || bid <= 0) return BadRequest("No business assigned to this user.");
            if (existing.BusinessId != bid) return Forbid();
        }
        return Ok(await repository.DeleteAsync(id));
    }
}
