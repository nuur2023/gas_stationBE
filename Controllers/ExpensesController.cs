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
public class ExpensesController(
    IExpenseRepository repository,
    IStationRepository stationRepository,
    GasStationDBContext db) : ControllerBase
{
    private static string NormalizeExpenseType(string? raw)
    {
        var t = (raw ?? string.Empty).Trim();
        if (string.Equals(t, "exchange", StringComparison.OrdinalIgnoreCase)) return "Exchange";
        if (string.Equals(t, "cashOrUsdTaken", StringComparison.OrdinalIgnoreCase)) return "cashOrUsdTaken";
        return "Expense";
    }
    private static string NormalizeSideAction(string? raw)
    {
        var t = (raw ?? string.Empty).Trim();
        if (string.Equals(t, "management", StringComparison.OrdinalIgnoreCase)) return "Management";
        return "Operation";
    }
    private string ResolveSideAction(string? requested)
    {
        var fallback = User.IsInRole("SuperAdmin") || User.IsInRole("Admin") ? "Management" : "Operation";
        if (string.IsNullOrWhiteSpace(requested))
            return fallback;

        var normalized = NormalizeSideAction(requested);
        if (fallback == "Operation" && normalized == "Management")
            return "Operation";
        return normalized;
    }

    private static bool CurrencyCodeIsUsd(string? code) =>
        string.Equals((code ?? string.Empty).Trim(), "USD", StringComparison.OrdinalIgnoreCase);

    private async Task<Currency?> GetCurrencyByIdAsync(int id) =>
        await db.Currencies.AsNoTracking().FirstOrDefaultAsync(c => !c.IsDeleted && c.Id == id);

    private async Task<double?> GetActiveRateNumberAsync(int businessId) =>
        await db.Rates.AsNoTracking()
            .Where(r => !r.IsDeleted && r.BusinessId == businessId && r.Active)
            .OrderByDescending(r => r.Date)
            .ThenByDescending(r => r.Id)
            .Select(r => (double?)r.RateNumber)
            .FirstOrDefaultAsync();

    /// <summary>
    /// USD: amount lives in USD only (local 0). SSP / exchange: local + rate with optional manual USD on exchange.
    /// Uses the active business rate when the client sends rate 0 for non-exchange or when exchange omits rate.
    /// </summary>
    private async Task<(double local, double rate, double usd, IActionResult? err)> ResolveExpenseAmountsAsync(
        ExpenseWriteRequestViewModel dto,
        int targetBusinessId)
    {
        if (dto.CurrencyId <= 0)
            return (0, 0, 0, BadRequest("Currency is required."));

        var currency = await GetCurrencyByIdAsync(dto.CurrencyId);
        if (currency is null)
            return (0, 0, 0, BadRequest("Invalid currency."));

        if (!double.TryParse(dto.LocalAmount.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var localField))
            return (0, 0, 0, BadRequest("Invalid local amount."));

        if (!double.TryParse(dto.Rate.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var rateField))
            return (0, 0, 0, BadRequest("Invalid rate."));

        if (!double.TryParse(dto.AmountUsd.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var usdField))
            return (0, 0, 0, BadRequest("Invalid USD amount."));

        var expenseType = NormalizeExpenseType(dto.Type);
        var isExchangeType = string.Equals(expenseType, "Exchange", StringComparison.OrdinalIgnoreCase);
        var isUsd = CurrencyCodeIsUsd(currency.Code);

        if (isUsd)
        {
            var usd = Math.Round(localField, 2, MidpointRounding.AwayFromZero);
            if (usd < 0)
                return (0, 0, 0, BadRequest("USD amount must be zero or positive."));
            return (0, 0, usd, null);
        }

        if (localField < 0)
            return (0, 0, 0, BadRequest("Local amount must be zero or positive."));

        var local = Math.Round(localField, 2, MidpointRounding.AwayFromZero);
        var active = await GetActiveRateNumberAsync(targetBusinessId);
        var rate = rateField > 0
            ? Math.Round(rateField, 6, MidpointRounding.AwayFromZero)
            : Math.Round(active ?? 0, 6, MidpointRounding.AwayFromZero);

        if (rate <= 0)
            return (0, 0, 0, BadRequest("Set a rate, or add an active rate under Rates for this business."));

        var usdComputed = Math.Round(local / rate, 2, MidpointRounding.AwayFromZero);
        if (isExchangeType && usdField > 0)
            usdComputed = Math.Round(usdField, 2, MidpointRounding.AwayFromZero);

        return (local, rate, usdComputed, null);
    }

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

    private bool ResolveExpenseBusiness(ExpenseWriteRequestViewModel dto, out int targetBusinessId, out IActionResult? err)
    {
        targetBusinessId = 0;
        err = null;
        if (IsSuperAdmin(User))
        {
            if (dto.BusinessId <= 0)
            {
                err = BadRequest("Select a business for this expense.");
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

    private async Task<IActionResult?> ValidateExpenseStationAsync(int targetBusinessId, int stationId)
    {
        var st = await stationRepository.GetByIdAsync(stationId);
        if (st is null || st.BusinessId != targetBusinessId)
        {
            return BadRequest("Station does not belong to the selected business.");
        }

        if (TryGetJwtStation(out var js) && js > 0 && stationId != js)
        {
            return BadRequest("You can only record expenses for your assigned station.");
        }

        return null;
    }

    /// <summary>
    /// Resolves the StationId to persist for an expense. Management entries are business-level
    /// and ignore any incoming station id (they always store NULL). Operation entries require
    /// a valid station that belongs to the target business.
    /// </summary>
    private async Task<(int? stationId, IActionResult? error)> ResolveExpenseStationAsync(
        ExpenseWriteRequestViewModel dto,
        string sideAction,
        int targetBusinessId)
    {
        if (string.Equals(sideAction, "Management", StringComparison.OrdinalIgnoreCase))
            return (null, null);

        var requested = dto.StationId ?? 0;
        if (requested <= 0)
            return (null, BadRequest("Operation expenses require a station."));

        var bad = await ValidateExpenseStationAsync(targetBusinessId, requested);
        return (bad is null ? requested : null, bad);
    }

    [HttpGet]
    public async Task<IActionResult> GetPaged(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? q = null,
        [FromQuery] int? filterStationId = null,
        [FromQuery] string? type = null,
        [FromQuery] string? sideAction = null)
    {
        var normalizedType = string.IsNullOrWhiteSpace(type) ? null : NormalizeExpenseType(type);
        var normalizedSideAction = ResolveSideAction(sideAction);
        // Management entries are stored business-wide with NULL StationId, so any station filter
        // (including the JWT fallback for staff users) would hide every row. Force null here.
        var isManagementList = string.Equals(normalizedSideAction, "Management", StringComparison.OrdinalIgnoreCase);

        if (IsSuperAdmin(User))
        {
            int? stationFilter = isManagementList ? null : filterStationId;
            return Ok(await repository.GetPagedAsync(page, pageSize, q, null, stationFilter, normalizedType, normalizedSideAction));
        }

        if (!TryGetJwtBusiness(out var bid))
        {
            return BadRequest("No business assigned to this user.");
        }

        int? scopedStation = isManagementList
            ? null
            : ListStationFilter.ForNonSuperAdmin(User, filterStationId);
        return Ok(await repository.GetPagedAsync(page, pageSize, q, bid, scopedStation, normalizedType, normalizedSideAction));
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
    public async Task<IActionResult> Create([FromBody] ExpenseWriteRequestViewModel dto)
    {
        if (!TryGetUserId(out var userId, out var uerr))
        {
            return uerr!;
        }

        if (!ResolveExpenseBusiness(dto, out var targetBusinessId, out var bizErr))
        {
            return bizErr!;
        }

        var resolvedSideAction = ResolveSideAction(dto.SideAction);
        var (resolvedStationId, stationErr) = await ResolveExpenseStationAsync(dto, resolvedSideAction, targetBusinessId);
        if (stationErr is not null)
        {
            return stationErr;
        }

        var (local, rate, usd, amtErr) = await ResolveExpenseAmountsAsync(dto, targetBusinessId);
        if (amtErr is not null)
            return amtErr;

        var entity = new Expense
        {
            Type = NormalizeExpenseType(dto.Type),
            SideAction = resolvedSideAction,
            Date = dto.Date?.UtcDateTime ?? DateTime.UtcNow,
            Description = dto.Description,
            CurrencyId = dto.CurrencyId,
            LocalAmount = local,
            Rate = rate,
            AmountUsd = usd,
            UserId = userId,
            BusinessId = targetBusinessId,
            StationId = resolvedStationId,
        };

        return Ok(await repository.AddAsync(entity));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] ExpenseWriteRequestViewModel dto)
    {
        if (!ResolveExpenseBusiness(dto, out var targetBusinessId, out var bizErr))
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
                return BadRequest("Expense belongs to a different business.");
            }
        }
        else if (!TryGetJwtBusiness(out var bid) || existing.BusinessId != bid)
        {
            return Forbid();
        }

        var resolvedSideAction = ResolveSideAction(dto.SideAction);
        var (resolvedStationId, stationErr) = await ResolveExpenseStationAsync(dto, resolvedSideAction, targetBusinessId);
        if (stationErr is not null)
        {
            return stationErr;
        }

        var (local, rate, usd, amtErr) = await ResolveExpenseAmountsAsync(dto, targetBusinessId);
        if (amtErr is not null)
            return amtErr;

        existing.Description = dto.Description;
        existing.Type = NormalizeExpenseType(dto.Type);
        existing.SideAction = resolvedSideAction;
        existing.CurrencyId = dto.CurrencyId;
        existing.LocalAmount = local;
        existing.Rate = rate;
        existing.AmountUsd = usd;
        existing.StationId = resolvedStationId;
        if (dto.Date.HasValue)
            existing.Date = dto.Date.Value.UtcDateTime;

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

        if (IsSuperAdmin(User))
        {
            return Ok(await repository.DeleteAsync(id));
        }

        if (!TryGetJwtBusiness(out var businessId))
        {
            return BadRequest("No business assigned to this user.");
        }

        if (existing.BusinessId != businessId)
        {
            return Forbid();
        }

        return Ok(await repository.DeleteAsync(id));
    }
}
