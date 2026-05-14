using System.Security.Claims;
using gas_station.Data.Interfaces;
using gas_station.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace gas_station.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FuelTypesController(IFuelTypeRepository repository) : ControllerBase
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

    /// <summary>
    /// SuperAdmin: optional <c>businessId</c> query filters the list; omit to return all businesses' types.
    /// All other roles: list is limited to the JWT <c>business_id</c> (query is ignored).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int? businessId = null)
    {
        if (IsSuperAdmin(User))
            return Ok(await repository.GetAllAsync(businessId));

        if (!TryGetJwtBusiness(out var bid))
            return BadRequest("No business assigned to this user.");

        return Ok(await repository.GetAllAsync(bid));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var entity = await repository.GetByIdAsync(id);
        if (entity == null)
            return NotFound();

        if (!IsSuperAdmin(User))
        {
            if (!TryGetJwtBusiness(out var bid) || entity.BusinessId != bid)
                return Forbid();
        }

        return Ok(entity);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] FuelType model)
    {
        if (!IsSuperAdmin(User))
        {
            if (!TryGetJwtBusiness(out var bid))
                return BadRequest("No business assigned to this user.");
            model.BusinessId = bid;
        }
        else if (model.BusinessId <= 0)
        {
            return BadRequest("BusinessId is required.");
        }

        return Ok(await repository.AddAsync(model));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] FuelType model)
    {
        var existing = await repository.GetByIdAsync(id);
        if (existing == null)
            return NotFound();

        if (!IsSuperAdmin(User))
        {
            if (!TryGetJwtBusiness(out var bid) || existing.BusinessId != bid)
                return Forbid();
            model.BusinessId = bid;
        }

        return Ok(await repository.UpdateAsync(id, model));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var existing = await repository.GetByIdAsync(id);
        if (existing == null)
            return NotFound();

        if (!IsSuperAdmin(User))
        {
            if (!TryGetJwtBusiness(out var bid) || existing.BusinessId != bid)
                return Forbid();
        }

        return Ok(await repository.DeleteAsync(id));
    }
}
