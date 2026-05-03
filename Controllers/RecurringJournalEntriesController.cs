using System.Globalization;
using System.Security.Claims;
using gas_station.Common;
using gas_station.Data.Context;
using gas_station.Models;
using gas_station.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace gas_station.Controllers;

[ApiController]
[Route("api/recurring-journal-entries")]
[Authorize]
public class RecurringJournalEntriesController(GasStationDBContext db) : ControllerBase
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

    /// <summary>
    /// When confirm-when-due is enabled and the template is already due, set pending immediately so the client
    /// does not wait for the recurring-journal background service (which runs on a long interval).
    /// </summary>
    private static bool IsCalendarDueForConfirmWhenDue(RecurringJournalEntry e, DateOnly todayUtc)
    {
        if (!e.AutoPost || e.IsPaused || !e.ConfirmWhenDue || e.PendingConfirmationRunDate != null)
            return false;
        if (e.NextRunDate == null)
            return false;
        var next = DateOnly.FromDateTime(e.NextRunDate.Value);
        var start = DateOnly.FromDateTime(e.StartDate);
        if (next > todayUtc || start > todayUtc)
            return false;
        if (e.EndDate is { } ed && DateOnly.FromDateTime(ed) < todayUtc)
            return false;
        return true;
    }

    /// <summary>
    /// Marks <see cref="RecurringJournalEntry.PendingConfirmationRunDate"/> when due. Does not consult
    /// <see cref="AccountingPeriodGuard"/> — confirm-post still blocks closed periods.
    /// </summary>
    private void TryMarkPendingConfirmationIfDue(RecurringJournalEntry e)
    {
        var todayUtc = DateOnly.FromDateTime(DateTime.UtcNow);
        if (!IsCalendarDueForConfirmWhenDue(e, todayUtc))
            return;

        e.PendingConfirmationRunDate = e.NextRunDate!.Value;
        e.UpdatedAt = DateTime.UtcNow;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int businessId, [FromQuery] int? filterStationId = null)
    {
        if (!ResolveBusiness(businessId, out var bid, out var err)) return err!;
        int? stationScope = null;
        if (IsSuperAdmin(User))
        {
            if (filterStationId is > 0)
                stationScope = filterStationId;
        }
        else
            stationScope = ListStationFilter.ForNonSuperAdmin(User, filterStationId);

        var q = db.RecurringJournalEntries.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BusinessId == bid);
        if (stationScope is > 0)
            q = q.Where(x => x.StationId == stationScope);

        var rows = await q
            .OrderBy(x => x.Name)
            .Select(x => new
            {
                x.Id,
                x.BusinessId,
                x.StationId,
                x.Name,
                x.DebitAccountId,
                x.CreditAccountId,
                x.Amount,
                frequency = (byte)x.Frequency,
                x.StartDate,
                x.EndDate,
                x.AutoPost,
                x.IsPaused,
                x.ConfirmWhenDue,
                x.PendingConfirmationRunDate,
                x.SupplierId,
                x.CustomerFuelGivenId,
                x.LastRunDate,
                x.NextRunDate,
                x.PostingUserId,
            })
            .ToListAsync();
        return Ok(rows);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var row = await db.RecurringJournalEntries.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (row is null) return NotFound();
        if (!IsSuperAdmin(User) && (!TryGetJwtBusiness(out var bid) || row.BusinessId != bid)) return Forbid();
        return Ok(row);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] RecurringJournalEntryWriteViewModel dto)
    {
        if (!ResolveBusiness(dto.BusinessId, out var bid, out var err)) return err!;
        if (!TryGetUserId(out var uid)) return Unauthorized();
        if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest("Name is required.");
        if (dto.DebitAccountId == dto.CreditAccountId) return BadRequest("Debit and credit accounts must differ.");
        if (!double.TryParse(dto.Amount.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var amt) || amt <= 0)
            return BadRequest("Amount must be greater than zero.");
        if (dto.ConfirmWhenDue && !dto.AutoPost)
            return BadRequest("Confirm when due requires Auto-post to be enabled.");

        var debit = await db.Accounts.Include(a => a.ChartsOfAccounts)
            .FirstOrDefaultAsync(a => a.Id == dto.DebitAccountId && !a.IsDeleted && (a.BusinessId == null || a.BusinessId == bid));
        var credit = await db.Accounts.Include(a => a.ChartsOfAccounts)
            .FirstOrDefaultAsync(a => a.Id == dto.CreditAccountId && !a.IsDeleted && (a.BusinessId == null || a.BusinessId == bid));
        if (debit is null || credit is null) return BadRequest("Invalid account for this business.");
        if (!AccountingAccountRules.IsDebitSideForRecurring(debit)) return BadRequest("Debit account must be Asset, Expense, or COGS.");
        if (!AccountingAccountRules.IsCreditSideForRecurring(credit)) return BadRequest("Credit account must be Asset, Liability, or Equity.");

        var start = (dto.StartDate ?? DateTimeOffset.UtcNow).UtcDateTime.Date;
        var end = dto.EndDate?.UtcDateTime.Date;
        if (end.HasValue && end.Value < start) return BadRequest("End date must be on or after start date.");

        var freq = (RecurringJournalFrequency)Math.Clamp(dto.Frequency, (byte)0, (byte)3);
        var postingUser = dto.PostingUserId > 0 ? dto.PostingUserId : uid;
        var pu = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == postingUser && !u.IsDeleted);
        if (pu is null) return BadRequest("Invalid posting user.");

        if (dto.CustomerFuelGivenId is > 0)
        {
            var ok = await db.CustomerFuelGivens.AnyAsync(c => c.Id == dto.CustomerFuelGivenId && !c.IsDeleted && c.BusinessId == bid);
            if (!ok) return BadRequest("Invalid customer for this business.");
        }
        if (dto.SupplierId is > 0)
        {
            var ok = await db.Suppliers.AnyAsync(s => s.Id == dto.SupplierId && !s.IsDeleted && s.BusinessId == bid);
            if (!ok) return BadRequest("Invalid supplier for this business.");
        }
        if (dto.StationId is > 0)
        {
            var stOk = await db.Stations.AnyAsync(s => s.Id == dto.StationId && !s.IsDeleted && s.BusinessId == bid);
            if (!stOk) return BadRequest("Invalid station for this business.");
        }

        var entity = new RecurringJournalEntry
        {
            BusinessId = bid,
            StationId = dto.StationId is > 0 ? dto.StationId : null,
            Name = dto.Name.Trim(),
            DebitAccountId = dto.DebitAccountId,
            CreditAccountId = dto.CreditAccountId,
            Amount = amt,
            Frequency = freq,
            StartDate = start,
            EndDate = end,
            AutoPost = dto.AutoPost,
            IsPaused = dto.IsPaused,
            ConfirmWhenDue = dto.ConfirmWhenDue,
            PendingConfirmationRunDate = null,
            SupplierId = dto.SupplierId is > 0 ? dto.SupplierId : null,
            CustomerFuelGivenId = dto.CustomerFuelGivenId is > 0 ? dto.CustomerFuelGivenId : null,
            PostingUserId = postingUser,
            NextRunDate = start,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.RecurringJournalEntries.Add(entity);
        TryMarkPendingConfirmationIfDue(entity);
        await db.SaveChangesAsync();
        return Ok(entity);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] RecurringJournalEntryWriteViewModel dto)
    {
        if (!ResolveBusiness(dto.BusinessId, out var bid, out var err)) return err!;
        if (!TryGetUserId(out var uid)) return Unauthorized();
        var row = await db.RecurringJournalEntries.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (row is null) return NotFound();
        if (row.BusinessId != bid) return Forbid();

        if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest("Name is required.");
        if (dto.DebitAccountId == dto.CreditAccountId) return BadRequest("Debit and credit accounts must differ.");
        if (!double.TryParse(dto.Amount.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var amt) || amt <= 0)
            return BadRequest("Amount must be greater than zero.");
        if (dto.ConfirmWhenDue && !dto.AutoPost)
            return BadRequest("Confirm when due requires Auto-post to be enabled.");

        var debit = await db.Accounts.Include(a => a.ChartsOfAccounts)
            .FirstOrDefaultAsync(a => a.Id == dto.DebitAccountId && !a.IsDeleted && (a.BusinessId == null || a.BusinessId == bid));
        var credit = await db.Accounts.Include(a => a.ChartsOfAccounts)
            .FirstOrDefaultAsync(a => a.Id == dto.CreditAccountId && !a.IsDeleted && (a.BusinessId == null || a.BusinessId == bid));
        if (debit is null || credit is null) return BadRequest("Invalid account for this business.");
        if (!AccountingAccountRules.IsDebitSideForRecurring(debit)) return BadRequest("Debit account must be Asset, Expense, or COGS.");
        if (!AccountingAccountRules.IsCreditSideForRecurring(credit)) return BadRequest("Credit account must be Asset, Liability, or Equity.");

        var start = (dto.StartDate ?? DateTimeOffset.UtcNow).UtcDateTime.Date;
        var end = dto.EndDate?.UtcDateTime.Date;
        if (end.HasValue && end.Value < start) return BadRequest("End date must be on or after start date.");

        var freq = (RecurringJournalFrequency)Math.Clamp(dto.Frequency, (byte)0, (byte)3);
        var postingUser = dto.PostingUserId > 0 ? dto.PostingUserId : uid;
        var pu = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == postingUser && !u.IsDeleted);
        if (pu is null) return BadRequest("Invalid posting user.");

        if (dto.CustomerFuelGivenId is > 0)
        {
            var ok = await db.CustomerFuelGivens.AnyAsync(c => c.Id == dto.CustomerFuelGivenId && !c.IsDeleted && c.BusinessId == bid);
            if (!ok) return BadRequest("Invalid customer for this business.");
        }
        if (dto.SupplierId is > 0)
        {
            var ok = await db.Suppliers.AnyAsync(s => s.Id == dto.SupplierId && !s.IsDeleted && s.BusinessId == bid);
            if (!ok) return BadRequest("Invalid supplier for this business.");
        }
        if (dto.StationId is > 0)
        {
            var stOk = await db.Stations.AnyAsync(s => s.Id == dto.StationId && !s.IsDeleted && s.BusinessId == bid);
            if (!stOk) return BadRequest("Invalid station for this business.");
        }

        row.Name = dto.Name.Trim();
        row.StationId = dto.StationId is > 0 ? dto.StationId : null;
        row.DebitAccountId = dto.DebitAccountId;
        row.CreditAccountId = dto.CreditAccountId;
        row.Amount = amt;
        row.Frequency = freq;
        row.StartDate = start;
        row.EndDate = end;
        row.AutoPost = dto.AutoPost;
        row.IsPaused = dto.IsPaused;
        if (!dto.ConfirmWhenDue)
            row.PendingConfirmationRunDate = null;
        row.ConfirmWhenDue = dto.ConfirmWhenDue;
        row.SupplierId = dto.SupplierId is > 0 ? dto.SupplierId : null;
        row.CustomerFuelGivenId = dto.CustomerFuelGivenId is > 0 ? dto.CustomerFuelGivenId : null;
        row.PostingUserId = postingUser;
        row.NextRunDate ??= start;
        var startDay = DateOnly.FromDateTime(start);
        if (row.NextRunDate != null && DateOnly.FromDateTime(row.NextRunDate.Value) < startDay)
            row.NextRunDate = start;
        row.UpdatedAt = DateTime.UtcNow;
        TryMarkPendingConfirmationIfDue(row);
        await db.SaveChangesAsync();
        return Ok(row);
    }

    /// <summary>Re-evaluates due + confirm-when-due and sets pending if appropriate (e.g. after list load).</summary>
    [HttpPost("{id:int}/ensure-pending-if-due")]
    public async Task<IActionResult> EnsurePendingIfDue(int id, [FromBody] RecurringJournalEnsurePendingViewModel dto)
    {
        if (!ResolveBusiness(dto.BusinessId, out var bid, out var err)) return err!;
        if (!TryGetUserId(out _)) return Unauthorized();
        var row = await db.RecurringJournalEntries.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (row is null) return NotFound();
        if (row.BusinessId != bid) return Forbid();

        TryMarkPendingConfirmationIfDue(row);
        await db.SaveChangesAsync();
        return Ok(row);
    }

    [HttpPost("{id:int}/confirm-post")]
    public async Task<IActionResult> ConfirmPost(int id, [FromBody] RecurringJournalConfirmPostViewModel dto)
    {
        if (!ResolveBusiness(dto.BusinessId, out var bid, out var err)) return err!;
        if (!TryGetUserId(out _)) return Unauthorized();
        var row = await db.RecurringJournalEntries
            .Include(x => x.DebitAccount).ThenInclude(a => a.ChartsOfAccounts)
            .Include(x => x.CreditAccount).ThenInclude(a => a.ChartsOfAccounts)
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (row is null) return NotFound();
        if (row.BusinessId != bid) return Forbid();
        if (row.PendingConfirmationRunDate == null)
            return BadRequest("This recurring entry is not waiting for confirmation.");
        if (!double.TryParse(dto.Amount.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var confirmAmt) || confirmAmt <= 0)
            return BadRequest("Amount must be greater than zero.");

        var runDate = row.PendingConfirmationRunDate.Value;
        if (await AccountingPeriodGuard.IsPostingBlockedAsync(db, row.BusinessId, runDate, JournalEntryKind.RecurringAuto, HttpContext.RequestAborted))
            return BadRequest("Posting is blocked for this accounting period.");

        try
        {
            await RecurringJournalPostingHelper.PostAndAdvanceAsync(
                db,
                row,
                runDate,
                confirmAmt,
                JournalEntryKind.RecurringAuto,
                HttpContext.RequestAborted);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }

        return Ok(row);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var row = await db.RecurringJournalEntries.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (row is null) return NotFound();
        if (!IsSuperAdmin(User))
        {
            if (!TryGetJwtBusiness(out var bid) || row.BusinessId != bid) return Forbid();
        }
        row.IsDeleted = true;
        row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok();
    }
}
