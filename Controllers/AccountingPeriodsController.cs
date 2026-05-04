using System.Security.Claims;
using gas_station.Data.Context;
using gas_station.Models;
using gas_station.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace gas_station.Controllers;

[ApiController]
[Route("api/accounting-periods")]
[Authorize]
public class AccountingPeriodsController(GasStationDBContext db) : ControllerBase
{
    private const string SuperAdminRole = "SuperAdmin";
    private static bool IsSuperAdmin(ClaimsPrincipal user) => user.IsInRole(SuperAdminRole);

    private static bool CanReopenPeriods(ClaimsPrincipal user) =>
        IsSuperAdmin(user) || user.IsInRole("Admin") || user.IsInRole("Accountant");

    private bool TryGetJwtBusiness(out int businessId)
    {
        businessId = 0;
        var bidStr = User.FindFirstValue("business_id");
        return !string.IsNullOrEmpty(bidStr) && int.TryParse(bidStr, out businessId);
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

    private bool TryGetUserId(out int userId)
    {
        userId = 0;
        var uid = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return !string.IsNullOrEmpty(uid) && int.TryParse(uid, out userId);
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

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] AccountingPeriodUpdateViewModel dto)
    {
        var period = await db.AccountingPeriods.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (period is null) return NotFound();
        if (!IsSuperAdmin(User) && (!TryGetJwtBusiness(out var jwtBid) || period.BusinessId != jwtBid)) return Forbid();
        if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest("Name is required.");
        var ps = dto.PeriodStart.UtcDateTime.Date;
        var pe = dto.PeriodEnd.UtcDateTime.Date;
        if (pe < ps) return BadRequest("Period end must be on or after period start.");

        var overlap = await db.AccountingPeriods.AnyAsync(x =>
            !x.IsDeleted && x.BusinessId == period.BusinessId && x.Id != id
            && ps <= x.PeriodEnd.Date && pe >= x.PeriodStart.Date);
        if (overlap) return BadRequest("Another period overlaps these dates.");

        period.Name = dto.Name.Trim();
        period.PeriodStart = ps;
        period.PeriodEnd = pe;
        period.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(period);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var period = await db.AccountingPeriods.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (period is null) return NotFound();
        if (!IsSuperAdmin(User) && (!TryGetJwtBusiness(out var jwtBid) || period.BusinessId != jwtBid)) return Forbid();
        period.IsDeleted = true;
        period.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(new { message = "Period removed." });
    }

    /// <summary>Marks period closed after manual books close. Does not post a journal.</summary>
    [HttpPost("{id:int}/mark-closed")]
    public async Task<IActionResult> MarkClosed(int id, [FromBody] MarkAccountingPeriodClosedViewModel? dto)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var period = await db.AccountingPeriods.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (period is null) return NotFound();
        if (!IsSuperAdmin(User) && (!TryGetJwtBusiness(out var jwtBid) || period.BusinessId != jwtBid)) return Forbid();
        if (period.Status != AccountingPeriodStatus.Open)
            return BadRequest("Only open periods can be marked closed.");

        int? closeJeId = null;
        if (dto != null && dto.CloseJournalEntryId is int jid && jid > 0)
        {
            var okJe = await db.JournalEntries.AsNoTracking()
                .AnyAsync(e => !e.IsDeleted && e.Id == jid && e.BusinessId == period.BusinessId);
            if (!okJe) return BadRequest("Journal entry not found for this business.");
            closeJeId = jid;
        }

        period.Status = AccountingPeriodStatus.Closed;
        period.ClosedAt = DateTime.UtcNow;
        period.ClosedByUserId = userId;
        period.CloseJournalEntryId = closeJeId;
        period.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(new { message = "Period marked closed.", closeJournalEntryId = closeJeId });
    }

    [HttpPost("{id:int}/reopen")]
    public async Task<IActionResult> Reopen(int id)
    {
        if (!CanReopenPeriods(User)) return Forbid();
        var period = await db.AccountingPeriods.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (period is null) return NotFound();
        if (!IsSuperAdmin(User))
        {
            if (!TryGetJwtBusiness(out var jwtBid) || period.BusinessId != jwtBid) return Forbid();
        }
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
