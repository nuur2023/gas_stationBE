using System.Globalization;
using System.Security.Claims;
using gas_station.Common;
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
public class JournalEntriesController(
    IJournalEntryRepository repository,
    IAccountRepository accountRepository,
    GasStationDBContext dbContext) : ControllerBase
{
    private const string SuperAdminRole = "SuperAdmin";
    private static bool IsSuperAdmin(ClaimsPrincipal user) => user.IsInRole(SuperAdminRole);

    private bool TryGetJwtBusiness(out int businessId)
    {
        businessId = 0;
        var bidStr = User.FindFirstValue("business_id");
        return !string.IsNullOrEmpty(bidStr) && int.TryParse(bidStr, out businessId);
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
        [FromQuery] int? filterStationId = null)
    {
        if (IsSuperAdmin(User))
            return Ok(await repository.GetPagedAsync(page, pageSize, q, null, filterStationId));

        if (!TryGetJwtBusiness(out var bid))
            return BadRequest("No business assigned to this user.");

        var stationFilter = ListStationFilter.ForNonSuperAdmin(User, filterStationId);
        return Ok(await repository.GetPagedAsync(page, pageSize, q, bid, stationFilter));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var row = await repository.GetByIdAsync(id);
        if (row is null) return NotFound();
        if (!IsSuperAdmin(User) && (!TryGetJwtBusiness(out var bid) || row.BusinessId != bid)) return NotFound();
        return Ok(row);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] JournalEntryWriteRequestViewModel dto)
    {
        if (!ResolveBusiness(dto.BusinessId, out var bid, out var err)) return err!;
        if (!TryGetUserId(out var userId, out var uerr)) return uerr!;
        if (dto.Lines.Count == 0) return BadRequest("At least one journal line is required.");

        double debit = 0;
        double credit = 0;
        var parsed = new List<(int accountId, double debit, double credit, string? remark, int? customerId, int? supplierId)>();
        foreach (var l in dto.Lines)
        {
            if (!double.TryParse(l.Debit.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d)) d = 0;
            if (!double.TryParse(l.Credit.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var c)) c = 0;
            if (d < 0 || c < 0) return BadRequest("Debit/Credit cannot be negative.");
            if (d == 0 && c == 0) return BadRequest("Each line must have debit or credit.");

            var acc = await accountRepository.GetByIdAsync(l.AccountId);
            if (acc is null) return BadRequest($"Invalid account on line: {l.AccountId}.");

            var remark = string.IsNullOrWhiteSpace(l.Remark) ? null : l.Remark.Trim();
            int? custId = l.CustomerId is > 0 ? l.CustomerId : null;
            int? suppId = l.SupplierId is > 0 ? l.SupplierId : null;

            if (custId.HasValue && suppId.HasValue)
                return BadRequest("A journal line cannot have both Customer and Supplier.");

            var isAr = AccountingSubledgerRules.IsAccountsReceivable(acc);
            var isAp = AccountingSubledgerRules.IsAccountsPayable(acc);
            var isExpense = string.Equals(acc.ChartsOfAccounts?.Type, "Expense", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(acc.ChartsOfAccounts?.Type, "COGS", StringComparison.OrdinalIgnoreCase);

            if (isAr)
            {
                if (!custId.HasValue)
                    return BadRequest("Accounts Receivable lines require a customer.");
                if (suppId.HasValue)
                    return BadRequest("Supplier is not allowed on Accounts Receivable lines.");
                var cfg = await dbContext.CustomerFuelGivens.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == custId.Value && !x.IsDeleted && x.BusinessId == bid);
                if (cfg is null)
                    return BadRequest("Invalid customer for this business.");
            }
            else if (isAp)
            {
                if (!suppId.HasValue)
                    return BadRequest("Accounts Payable lines require a supplier.");
                if (custId.HasValue)
                    return BadRequest("Customer is not allowed on Accounts Payable lines.");
                var sup = await dbContext.Suppliers.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == suppId.Value && !x.IsDeleted && x.BusinessId == bid);
                if (sup is null)
                    return BadRequest("Invalid supplier for this business.");
            }
            else
            {
                if (custId.HasValue)
                    return BadRequest("Customer is only allowed on Accounts Receivable accounts.");
                if (suppId.HasValue && !isExpense)
                    return BadRequest("Supplier is only allowed on Accounts Payable or Expense/COGS accounts.");
                if (suppId.HasValue)
                {
                    var sup = await dbContext.Suppliers.AsNoTracking()
                        .FirstOrDefaultAsync(x => x.Id == suppId.Value && !x.IsDeleted && x.BusinessId == bid);
                    if (sup is null)
                        return BadRequest("Invalid supplier for this business.");
                }
            }

            debit += d;
            credit += c;
            parsed.Add((l.AccountId, d, c, remark, custId, suppId));
        }
        if (Math.Round(debit, 2) != Math.Round(credit, 2))
            return BadRequest("Journal entry must be balanced (total debit equals total credit).");

        var journalDate = dto.Date?.UtcDateTime ?? DateTime.UtcNow;
        var kind = JournalEntryKind.Normal;
        if (dto.EntryKind is { } ek && ek <= (byte)JournalEntryKind.RecurringAuto)
            kind = (JournalEntryKind)ek;

        if (await AccountingPeriodGuard.IsPostingBlockedAsync(dbContext, bid, journalDate, kind))
            return BadRequest("The journal date falls in a closed accounting period.");

        await using var tx = await dbContext.Database.BeginTransactionAsync();
        try
        {
            var row = await AccountingPostingHelper.CreateJournalEntryAsync(
                dbContext,
                journalDate,
                dto.Description.Trim(),
                bid,
                userId,
                dto.StationId,
                parsed,
                kind,
                null);
            await tx.CommitAsync();
            return Ok(await repository.GetByIdAsync(row.Id));
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var row = await repository.GetByIdAsync(id);
        if (row is null) return NotFound();
        if (!IsSuperAdmin(User) && (!TryGetJwtBusiness(out var bid) || row.BusinessId != bid)) return Forbid();
        return Ok(await repository.DeleteAsync(id));
    }
}

