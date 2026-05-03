using System.Security.Claims;
using gas_station.Data.Context;
using gas_station.Models;
using gas_station.Services;
using gas_station.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace gas_station.Controllers;

[ApiController]
[Route("api/accounting-periods")]
[Authorize]
public class AccountingPeriodsController(GasStationDBContext db, PeriodCloseService closeService) : ControllerBase
{
    private const string SuperAdminRole = "SuperAdmin";
    private static bool IsSuperAdmin(ClaimsPrincipal user) => user.IsInRole(SuperAdminRole);

    private bool TryGetJwtBusiness(out int businessId)
    {
        businessId = 0;
        var bidStr = User.FindFirstValue("business_id");
        return !string.IsNullOrEmpty(bidStr) && int.TryParse(bidStr, out businessId);
    }

    private bool TryGetUserId(out int userId)
    {
        userId = 0;
        var uid = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return !string.IsNullOrEmpty(uid) && int.TryParse(uid, out userId);
    }

    private bool ResolveBusiness(int dtoBusinessId, out int bid, out IActionResult? err)
    {
        bid = 0;
        err = null;
        if (IsSuperAdmin(User))
        {
            if (dtoBusinessId <= 0) { err = BadRequest("Select a business."); return false; }
            bid = dtoBusinessId;
            return true;
        }
        if (!TryGetJwtBusiness(out var jwtBid)) { err = BadRequest("No business assigned."); return false; }
        if (dtoBusinessId > 0 && dtoBusinessId != jwtBid) { err = Forbid(); return false; }
        bid = jwtBid;
        return true;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int businessId)
    {
        if (!ResolveBusiness(businessId, out var bid, out var err)) return err!;
        var rows = await db.AccountingPeriods.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BusinessId == bid)
            .OrderByDescending(x => x.PeriodStart)
            .Select(x => new
            {
                x.Id,
                x.BusinessId,
                x.Name,
                x.PeriodStart,
                x.PeriodEnd,
                status = (byte)x.Status,
                x.ClosedAt,
                x.ClosedByUserId,
                x.CloseJournalEntryId,
            })
            .ToListAsync();
        return Ok(rows);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] AccountingPeriodWriteViewModel dto)
    {
        if (!ResolveBusiness(dto.BusinessId, out var bid, out var err)) return err!;
        if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest("Name is required.");
        var ps = dto.PeriodStart.UtcDateTime.Date;
        var pe = dto.PeriodEnd.UtcDateTime.Date;
        if (pe < ps) return BadRequest("Period end must be on or after period start.");

        var overlap = await db.AccountingPeriods.AnyAsync(x =>
            !x.IsDeleted && x.BusinessId == bid && ps <= x.PeriodEnd.Date && pe >= x.PeriodStart.Date);
        if (overlap) return BadRequest("A period already exists that overlaps these dates.");

        var entity = new AccountingPeriod
        {
            BusinessId = bid,
            Name = dto.Name.Trim(),
            PeriodStart = ps,
            PeriodEnd = pe,
            Status = AccountingPeriodStatus.Open,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.AccountingPeriods.Add(entity);
        await db.SaveChangesAsync();
        return Ok(entity);
    }

    [HttpPost("{id:int}/close")]
    public async Task<IActionResult> Close(int id)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var period = await db.AccountingPeriods.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (period is null) return NotFound();
        if (!IsSuperAdmin(User) && (!TryGetJwtBusiness(out var bid) || period.BusinessId != bid)) return Forbid();

        var (ok, error, journalId) = await closeService.ClosePeriodAsync(id, userId);
        if (!ok) return BadRequest(error);
        return Ok(new { journalId, message = "Period closed." });
    }

    [HttpPost("{id:int}/reopen")]
    public async Task<IActionResult> Reopen(int id)
    {
        if (!IsSuperAdmin(User)) return Forbid();
        var period = await db.AccountingPeriods.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (period is null) return NotFound();
        if (period.Status != AccountingPeriodStatus.Closed) return BadRequest("Only closed periods can be reopened.");
        period.Status = AccountingPeriodStatus.Open;
        period.ClosedAt = null;
        period.ClosedByUserId = null;
        period.CloseJournalEntryId = null;
        period.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(new { message = "Period reopened (closing journal not reversed — adjust manually if needed)." });
    }
}
