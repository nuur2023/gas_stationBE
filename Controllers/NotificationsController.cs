using System.Security.Claims;
using gas_station.Data.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace gas_station.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class NotificationsController(INotificationsRepository repository) : ControllerBase
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

    private bool ResolveQueryBusiness(int? queryBusinessId, out int targetBusinessId, out IActionResult? err)
    {
        targetBusinessId = 0;
        err = null;
        if (IsSuperAdmin(User))
        {
            if (queryBusinessId is null or <= 0)
            {
                err = BadRequest("Select a business (businessId query).");
                return false;
            }

            targetBusinessId = queryBusinessId.Value;
            return true;
        }

        if (!TryGetJwtBusiness(out var bid))
        {
            err = BadRequest("No business assigned to this user.");
            return false;
        }

        if (queryBusinessId is > 0 && queryBusinessId != bid)
        {
            err = Forbid();
            return false;
        }

        targetBusinessId = bid;
        return true;
    }

    private int? JwtStationFilter() => TryGetJwtStation(out var st) && st > 0 ? st : null;

    [HttpGet]
    public async Task<IActionResult> GetPaged(
        [FromQuery] int? businessId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 30)
    {
        if (!ResolveQueryBusiness(businessId, out var bid, out var err))
            return err!;
        if (!TryGetUserId(out var userId, out var uerr))
            return uerr!;
        var jwtSt = JwtStationFilter();
        var dto = await repository.GetPagedForUserAsync(bid, userId, jwtSt, IsSuperAdmin(User), page, pageSize);
        return Ok(dto);
    }

    [HttpGet("unread-count")]
    public async Task<IActionResult> GetUnreadCount([FromQuery] int? businessId = null)
    {
        if (!ResolveQueryBusiness(businessId, out var bid, out var err))
            return err!;
        if (!TryGetUserId(out var userId, out var uerr))
            return uerr!;
        var jwtSt = JwtStationFilter();
        var n = await repository.CountUnreadAsync(bid, userId, jwtSt, IsSuperAdmin(User));
        return Ok(new { count = n });
    }

    [HttpPost("{id:int}/read")]
    public async Task<IActionResult> MarkRead(int id, [FromQuery] int? businessId = null)
    {
        if (!ResolveQueryBusiness(businessId, out var bid, out var err))
            return err!;
        if (!TryGetUserId(out var userId, out var uerr))
            return uerr!;
        var jwtSt = JwtStationFilter();
        var ok = await repository.MarkReadAsync(id, bid, userId, jwtSt, IsSuperAdmin(User));
        return ok ? NoContent() : NotFound();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead([FromQuery] int? businessId = null)
    {
        if (!ResolveQueryBusiness(businessId, out var bid, out var err))
            return err!;
        if (!TryGetUserId(out var userId, out var uerr))
            return uerr!;
        var jwtSt = JwtStationFilter();
        var n = await repository.MarkAllReadAsync(bid, userId, jwtSt, IsSuperAdmin(User));
        return Ok(new { marked = n });
    }
}
