using System.Security.Claims;
using backend.Data.Interfaces;
using backend.Models;
using backend.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace backend.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SuppliersController(ISupplierRepository repository) : ControllerBase
{
    private const string SuperAdminRole = "SuperAdmin";

    private static bool IsSuperAdmin(ClaimsPrincipal user) => user.IsInRole(SuperAdminRole);

    private bool TryGetJwtBusiness(out int businessId)
    {
        businessId = 0;
        var bidStr = User.FindFirstValue("business_id");
        return !string.IsNullOrEmpty(bidStr) && int.TryParse(bidStr, out businessId);
    }

    private bool ResolveSupplierBusiness(SupplierWriteRequestViewModel dto, out int targetBusinessId, out IActionResult? err)
    {
        targetBusinessId = 0;
        err = null;
        if (IsSuperAdmin(User))
        {
            if (dto.BusinessId <= 0)
            {
                err = BadRequest("Select a business for this supplier.");
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
    public async Task<IActionResult> Create([FromBody] SupplierWriteRequestViewModel dto)
    {
        if (!ResolveSupplierBusiness(dto, out var targetBusinessId, out var bizErr))
        {
            return bizErr!;
        }

        var entity = new Supplier
        {
            Name = (dto.Name ?? string.Empty).Trim(),
            Phone = (dto.Phone ?? string.Empty).Trim(),
            Address = (dto.Address ?? string.Empty).Trim(),
            Email = (dto.Email ?? string.Empty).Trim(),
            BusinessId = targetBusinessId,
        };

        return Ok(await repository.AddAsync(entity));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] SupplierWriteRequestViewModel dto)
    {
        if (!ResolveSupplierBusiness(dto, out var targetBusinessId, out var bizErr))
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
                return BadRequest("Supplier belongs to a different business.");
            }
        }
        else if (!TryGetJwtBusiness(out var bid) || existing.BusinessId != bid)
        {
            return NotFound();
        }

        existing.Name = (dto.Name ?? string.Empty).Trim();
        existing.Phone = (dto.Phone ?? string.Empty).Trim();
        existing.Address = (dto.Address ?? string.Empty).Trim();
        existing.Email = (dto.Email ?? string.Empty).Trim();
        existing.BusinessId = targetBusinessId;

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
