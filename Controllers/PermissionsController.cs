using System.Security.Claims;
using gas_station.Data.Context;
using gas_station.Data.Interfaces;
using gas_station.Models;
using gas_station.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace gas_station.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PermissionsController(
    IPermissionRepository repository,
    GasStationDBContext db) : ControllerBase
{
    private const string SuperAdminRole = "SuperAdmin";
    private const string AdminRole = "Admin";

    /// <summary>Submenu routes SuperAdmin must not assign to Admin users (global setup / chart of accounts).</summary>
    private static readonly HashSet<string> AdminGrantBlockedSubmenuPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/accounting/charts-of-accounts",
        "/setup/roles",
        
        "/setup/businesses",
        "/stations",
        "/setup/menus",
        "/setup/submenus",
        "/setup/currencies",
    };

    /// <summary>Submenu routes an Admin user must not delegate to others (only SuperAdmin can assign these).</summary>
    private static readonly HashSet<string> AdminDelegateBlockedSubmenuPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/setup/permissions",
        "/setup/business-users",
    };

    /// <summary>Pool / transfer UI routes — hidden when the business does not support pooling.</summary>
    private static readonly HashSet<string> BusinessPoolSubmenuPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/fuel-inventory",
        "/transfers",
        "/transfer-audit-trail",
        "/fuel-inventory/transfers",
    };

    private static bool IsSuperAdmin(ClaimsPrincipal user) => user.IsInRole(SuperAdminRole);

    private static bool BypassNavPermissions(ClaimsPrincipal user) =>
        user.IsInRole(SuperAdminRole);

    private bool TryGetJwtUserId(out int userId, out IActionResult? error)
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

    private async Task<Dictionary<(int menuId, int? subMenuId), Permission>> GetActorPermissionMapAsync(int businessId)
    {
        if (!TryGetJwtUserId(out var actorUserId, out _))
            return [];

        var rows = await db.Permissions.AsNoTracking()
            .Where(p => !p.IsDeleted && p.UserId == actorUserId && p.BusinessId == businessId)
            .ToListAsync();

        return rows.ToDictionary(
            p => (p.MenuId, p.SubMenuId),
            p => p);
    }

    private bool TryGetJwtBusiness(out int businessId)
    {
        businessId = 0;
        var bidStr = User.FindFirstValue("business_id");
        return !string.IsNullOrEmpty(bidStr) && int.TryParse(bidStr, out businessId);
    }

    /// <summary>Users linked to a business (for permission assignment UI). SuperAdmin must pass businessId; others use JWT business.</summary>
    [HttpGet("context-users")]
    public async Task<IActionResult> GetContextUsers([FromQuery] int? businessId = null)
    {
        int bid;
        if (IsSuperAdmin(User))
        {
            if (businessId is null or <= 0)
                return BadRequest("businessId is required.");
            bid = businessId.Value;
        }
        else
        {
            if (!TryGetJwtBusiness(out bid) || bid <= 0)
                return BadRequest("No business assigned to this user.");
        }

        var userIds = await db.BusinessUsers.AsNoTracking()
            .Where(bu => !bu.IsDeleted && bu.BusinessId == bid)
            .Select(bu => bu.UserId)
            .Distinct()
            .ToListAsync();

        var users = await db.Users.AsNoTracking()
            .Where(u => !u.IsDeleted && userIds.Contains(u.Id))
            .OrderBy(u => u.Name)
            .Select(u => new
            {
                u.Id,
                u.Name,
                u.Email,
                u.RoleId,
                RoleName = u.Role != null ? u.Role.Name : null
            })
            .ToListAsync();

        return Ok(users);
    }

    /// <summary>Current user's menu access for the JWT business (sidebar + route guard). Only SuperAdmin gets full access.</summary>
    [HttpGet("me")]
    public async Task<IActionResult> GetMyPermissions()
    {
        if (BypassNavPermissions(User))
            return Ok(new PermissionMeResponseViewModel { FullAccess = true, SupportsPool = true, Items = [] });

        if (!TryGetJwtUserId(out var uid, out var uerr))
            return uerr!;

        if (!TryGetJwtBusiness(out var bid) || bid <= 0)
            return Ok(new PermissionMeResponseViewModel { FullAccess = false, SupportsPool = true, Items = [] });

        var biz = await db.Businesses.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == bid && !b.IsDeleted);
        var supportsPool = biz?.IsSupportPool ?? true;

        var rows = await db.Permissions.AsNoTracking()
            .Where(p => !p.IsDeleted && p.UserId == uid && p.BusinessId == bid)
            .Include(p => p.Menu)
            .Include(p => p.SubMenu)
            .ToListAsync();

        var flat = rows
            .Select(p =>
            {
                var route = p.SubMenu != null && !string.IsNullOrWhiteSpace(p.SubMenu.Route)
                    ? p.SubMenu.Route.Trim()
                    : p.Menu != null && !string.IsNullOrWhiteSpace(p.Menu.Route)
                        ? p.Menu.Route.Trim()
                        : "";
                return new PermissionMeItemViewModel
                {
                    Route = route,
                    CanView = p.CanView,
                    CanCreate = p.CanCreate,
                    CanUpdate = p.CanUpdate,
                    CanDelete = p.CanDelete,
                };
            })
            .Where(x => x.Route.Length > 0)
            .Where(x => supportsPool || !BusinessPoolSubmenuPaths.Contains(RoutePathOnly(x.Route)))
            .ToList();

        var items = flat
            .GroupBy(x => x.Route, StringComparer.Ordinal)
            .Select(g => new PermissionMeItemViewModel
            {
                Route = g.Key,
                CanView = g.Any(x => x.CanView),
                CanCreate = g.Any(x => x.CanCreate),
                CanUpdate = g.Any(x => x.CanUpdate),
                CanDelete = g.Any(x => x.CanDelete),
            })
            .ToList();

        return Ok(new PermissionMeResponseViewModel { FullAccess = false, SupportsPool = supportsPool, Items = items });
    }

    private static string RoutePathOnly(string? route)
    {
        if (string.IsNullOrWhiteSpace(route)) return "";
        var t = route.Trim();
        var q = t.IndexOf('?', StringComparison.Ordinal);
        return q >= 0 ? t[..q] : t;
    }

    private async Task<bool> TargetUserIsAdminAsync(int userId) =>
        await (
            from u in db.Users.AsNoTracking()
            join r in db.Roles.AsNoTracking() on u.RoleId equals r.Id
            where !u.IsDeleted && u.Id == userId && r.Name == AdminRole
            select u.Id).AnyAsync();

    private async Task<List<Permission>> RemoveSubmenusByBlockedRoutesAsync(
        List<Permission> list,
        HashSet<string> blockedPaths)
    {
        var subIds = list.Where(p => p.SubMenuId is > 0).Select(p => p.SubMenuId!.Value).Distinct().ToList();
        if (subIds.Count == 0) return list;

        var idToRoute = await db.SubMenus.AsNoTracking()
            .Where(s => subIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Route })
            .ToListAsync();
        var map = idToRoute.ToDictionary(x => x.Id, x => x.Route ?? "");

        return list.Where(p =>
        {
            if (p.SubMenuId is null or <= 0) return true;
            if (!map.TryGetValue(p.SubMenuId.Value, out var r)) return true;
            var path = RoutePathOnly(r);
            return !blockedPaths.Contains(path);
        }).ToList();
    }

    private async Task<IActionResult?> EnsureCanManagePermissionsAsync(int targetUserId, int businessId)
    {
        var linked = await db.BusinessUsers.AsNoTracking()
            .AnyAsync(bu => !bu.IsDeleted && bu.UserId == targetUserId && bu.BusinessId == businessId);
        if (!linked)
            return BadRequest("Selected user is not linked to this business.");

        if (IsSuperAdmin(User))
            return null;

        if (!TryGetJwtBusiness(out var jwtBid) || jwtBid != businessId)
            return Forbid();

        return null;
    }

    [HttpGet]
    public async Task<IActionResult> GetPaged([FromQuery] int page = 1, [FromQuery] int pageSize = 50, [FromQuery] string? q = null)
        => Ok(await repository.GetPagedAsync(page, pageSize, q));

    [HttpGet("by-user")]
    public async Task<IActionResult> GetByUser([FromQuery] int userId, [FromQuery] int businessId)
    {
        var err = await EnsureCanManagePermissionsAsync(userId, businessId);
        if (err != null) return err;
        return Ok(await repository.GetByUserAndBusinessAsync(userId, businessId));
    }

    [HttpPost("bulk")]
    public async Task<IActionResult> BulkSave([FromBody] BulkPermissionsRequestViewModel dto)
    {
        var err = await EnsureCanManagePermissionsAsync(dto.UserId, dto.BusinessId);
        if (err != null) return err;

        var actorMap = IsSuperAdmin(User)
            ? new Dictionary<(int menuId, int? subMenuId), Permission>()
            : await GetActorPermissionMapAsync(dto.BusinessId);

        var list = dto.Items
            .Select(i =>
            {
                var requested = new Permission
                {
                    MenuId = i.MenuId,
                    SubMenuId = i.SubMenuId,
                    CanView = i.CanView,
                    CanCreate = i.CanCreate,
                    CanUpdate = i.CanUpdate,
                    CanDelete = i.CanDelete,
                };

                if (IsSuperAdmin(User))
                    return requested;

                actorMap.TryGetValue((i.MenuId, i.SubMenuId), out var actorAllowed);
                var capView = actorAllowed?.CanView ?? false;
                var capCreate = actorAllowed?.CanCreate ?? false;
                var capUpdate = actorAllowed?.CanUpdate ?? false;
                var capDelete = actorAllowed?.CanDelete ?? false;

                requested.CanView = requested.CanView && capView;
                requested.CanCreate = requested.CanCreate && capCreate;
                requested.CanUpdate = requested.CanUpdate && capUpdate;
                requested.CanDelete = requested.CanDelete && capDelete;
                return requested;
            })
            .ToList();

        var targetBiz = await db.Businesses.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == dto.BusinessId && !b.IsDeleted);
        if (targetBiz is { IsSupportPool: false })
            list = await RemoveSubmenusByBlockedRoutesAsync(list, BusinessPoolSubmenuPaths);

        if (IsSuperAdmin(User) && await TargetUserIsAdminAsync(dto.UserId))
            list = await RemoveSubmenusByBlockedRoutesAsync(list, AdminGrantBlockedSubmenuPaths);

        if (!IsSuperAdmin(User) && User.IsInRole(AdminRole))
            list = await RemoveSubmenusByBlockedRoutesAsync(list, AdminDelegateBlockedSubmenuPaths);

        await repository.ReplaceForUserAndBusinessAsync(dto.UserId, dto.BusinessId, list);
        return Ok();
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id) => Ok(await repository.GetByIdAsync(id));

    [HttpPost]
    public async Task<IActionResult> Create(Permission model) => Ok(await repository.AddAsync(model));

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, Permission model) => Ok(await repository.UpdateAsync(id, model));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id) => Ok(await repository.DeleteAsync(id));
}
