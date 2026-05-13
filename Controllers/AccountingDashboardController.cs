using System.Security.Claims;
using gas_station.Data.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace gas_station.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AccountingDashboardController(IAccountingDashboardRepository repository) : ControllerBase
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

    [HttpGet("overview")]
    public async Task<IActionResult> GetOverview(
        [FromQuery] int businessId,
        [FromQuery] int? stationId,
        CancellationToken cancellationToken)
    {
        if (!ResolveBusiness(businessId, out var bid, out var err)) return err!;
        var data = await repository.GetOverviewAsync(bid, stationId, cancellationToken).ConfigureAwait(false);
        return Ok(data);
    }

    [HttpGet("recent-transactions")]
    public async Task<IActionResult> GetRecentTransactions(
        [FromQuery] int businessId,
        [FromQuery] int? stationId,
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        if (!ResolveBusiness(businessId, out var bid, out var err)) return err!;
        if (from.Date > to.Date)
            return BadRequest("`from` must be on or before `to`.");
        if ((to.Date - from.Date).TotalDays > 366)
            return BadRequest("Date range cannot exceed 366 days.");
        var data = await repository
            .GetRecentTransactionsPagedAsync(bid, stationId, from, to, page, pageSize, cancellationToken)
            .ConfigureAwait(false);
        return Ok(data);
    }
}
