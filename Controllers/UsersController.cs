using gas_station.Data.Context;
using gas_station.Data.Interfaces;
using gas_station.Models;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace gas_station.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UsersController(IUserRepository repository, GasStationDBContext db) : ControllerBase
{
    private const string SuperAdminRole = "SuperAdmin";
    private const string AdminRole = "Admin";
    private bool IsSuperAdmin() => User.IsInRole(SuperAdminRole);
    private bool TryGetJwtBusiness(out int businessId)
    {
        businessId = 0;
        var bid = User.FindFirstValue("business_id");
        return !string.IsNullOrEmpty(bid) && int.TryParse(bid, out businessId);
    }

    private async Task<bool> UserInBusinessAsync(int userId, int businessId)
        => await db.BusinessUsers.AsNoTracking()
            .AnyAsync(bu => !bu.IsDeleted && bu.UserId == userId && bu.BusinessId == businessId);

    private async Task<bool> IsRoleNameAsync(int roleId, string roleName)
        => await db.Roles.AsNoTracking().AnyAsync(r => !r.IsDeleted && r.Id == roleId && r.Name == roleName);

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
        if (IsSuperAdmin()) return Ok(row);

        if (!TryGetJwtBusiness(out var bid) || bid <= 0)
            return BadRequest("No business assigned to this user.");

        if (await IsRoleNameAsync(row.RoleId, SuperAdminRole) || await IsRoleNameAsync(row.RoleId, AdminRole)) return Forbid();
        if (!await UserInBusinessAsync(row.Id, bid)) return Forbid();
        return Ok(row);
    }

    [HttpPost]
    public async Task<IActionResult> Create(User model)
    {
        if (!IsSuperAdmin())
        {
            if (await IsRoleNameAsync(model.RoleId, SuperAdminRole) || await IsRoleNameAsync(model.RoleId, AdminRole))
                return Forbid();
        }

        return Ok(await repository.AddAsync(model));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, User model)
    {
        if (!IsSuperAdmin())
        {
            if (!TryGetJwtBusiness(out var bid) || bid <= 0)
                return BadRequest("No business assigned to this user.");

            var existing = await repository.GetByIdAsync(id);
            if (existing is null) return NotFound();
            if (await IsRoleNameAsync(existing.RoleId, SuperAdminRole) || await IsRoleNameAsync(existing.RoleId, AdminRole)) return Forbid();
            if (!await UserInBusinessAsync(existing.Id, bid)) return Forbid();

            if (await IsRoleNameAsync(model.RoleId, SuperAdminRole) || await IsRoleNameAsync(model.RoleId, AdminRole))
                return Forbid();
        }

        return Ok(await repository.UpdateAsync(id, model));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (!IsSuperAdmin())
        {
            if (!TryGetJwtBusiness(out var bid) || bid <= 0)
                return BadRequest("No business assigned to this user.");

            var existing = await repository.GetByIdAsync(id);
            if (existing is null) return NotFound();
            if (await IsRoleNameAsync(existing.RoleId, SuperAdminRole) || await IsRoleNameAsync(existing.RoleId, AdminRole)) return Forbid();
            if (!await UserInBusinessAsync(existing.Id, bid)) return Forbid();
        }

        return Ok(await repository.DeleteAsync(id));
    }
}
