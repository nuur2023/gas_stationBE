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
public class EmployeesController(IEmployeeRepository repository) : ControllerBase
{
    private const string SuperAdminRole = "SuperAdmin";

    private static bool IsSuperAdmin(ClaimsPrincipal user) => user.IsInRole(SuperAdminRole);

    private bool TryGetJwtBusiness(out int businessId)
    {
        businessId = 0;
        var bidStr = User.FindFirstValue("business_id");
        return !string.IsNullOrEmpty(bidStr) && int.TryParse(bidStr, out businessId);
    }

    private bool ResolveBusiness(int dtoBusinessId, out int targetBusinessId, out IActionResult? err)
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

    [HttpGet]
    public async Task<IActionResult> GetPaged(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? q = null,
        [FromQuery] int? businessId = null,
        [FromQuery] int? filterStationId = null,
        [FromQuery] bool includeInactive = false)
    {
        if (IsSuperAdmin(User))
        {
            return Ok(await repository.GetPagedAsync(page, pageSize, q, businessId, filterStationId, includeInactive));
        }

        if (!TryGetJwtBusiness(out var bid))
            return BadRequest("No business assigned to this user.");

        var stationScope = ListStationFilter.ForNonSuperAdmin(User, filterStationId);
        return Ok(await repository.GetPagedAsync(page, pageSize, q, bid, stationScope, includeInactive));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var entity = await repository.GetByIdAsync(id);
        if (entity is null) return NotFound();
        if (!IsSuperAdmin(User))
        {
            if (!TryGetJwtBusiness(out var bid) || entity.BusinessId != bid)
                return NotFound();
        }
        return Ok(entity);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] EmployeeWriteRequestViewModel dto)
    {
        if (!ResolveBusiness(dto.BusinessId, out var targetBusinessId, out var bizErr))
            return bizErr!;

        var name = (dto.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name)) return BadRequest("Name is required.");

        if (!double.TryParse((dto.BaseSalary ?? "0").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var baseSalary) || baseSalary < 0)
            baseSalary = 0;

        var entity = new Employee
        {
            Name = name,
            Phone = (dto.Phone ?? string.Empty).Trim(),
            Email = (dto.Email ?? string.Empty).Trim(),
            Address = (dto.Address ?? string.Empty).Trim(),
            Position = (dto.Position ?? string.Empty).Trim(),
            BaseSalary = Math.Round(baseSalary, 2, MidpointRounding.AwayFromZero),
            IsActive = dto.IsActive,
            BusinessId = targetBusinessId,
            StationId = dto.StationId is > 0 ? dto.StationId : null,
        };

        return Ok(await repository.AddAsync(entity));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] EmployeeWriteRequestViewModel dto)
    {
        if (!ResolveBusiness(dto.BusinessId, out var targetBusinessId, out var bizErr))
            return bizErr!;

        var existing = await repository.GetByIdAsync(id);
        if (existing is null) return NotFound();

        if (IsSuperAdmin(User))
        {
            if (existing.BusinessId != targetBusinessId)
                return BadRequest("Employee belongs to a different business.");
        }
        else if (!TryGetJwtBusiness(out var bid) || existing.BusinessId != bid)
        {
            return NotFound();
        }

        var name = (dto.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name)) return BadRequest("Name is required.");

        if (!double.TryParse((dto.BaseSalary ?? "0").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var baseSalary) || baseSalary < 0)
            baseSalary = 0;

        existing.Name = name;
        existing.Phone = (dto.Phone ?? string.Empty).Trim();
        existing.Email = (dto.Email ?? string.Empty).Trim();
        existing.Address = (dto.Address ?? string.Empty).Trim();
        existing.Position = (dto.Position ?? string.Empty).Trim();
        existing.BaseSalary = Math.Round(baseSalary, 2, MidpointRounding.AwayFromZero);
        existing.IsActive = dto.IsActive;
        existing.BusinessId = targetBusinessId;
        existing.StationId = dto.StationId is > 0 ? dto.StationId : null;

        return Ok(await repository.UpdateAsync(id, existing));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var existing = await repository.GetByIdAsync(id);
        if (existing is null) return NotFound();

        if (!IsSuperAdmin(User))
        {
            if (!TryGetJwtBusiness(out var bid) || existing.BusinessId != bid)
                return NotFound();
        }

        return Ok(await repository.DeleteAsync(id));
    }
}
