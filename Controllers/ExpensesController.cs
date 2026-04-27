using System.Globalization;
using System.Security.Claims;
using System.Text.RegularExpressions;
using backend.Common;
using backend.Data.Interfaces;
using backend.Models;
using backend.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace backend.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ExpensesController(IExpenseRepository repository, IStationRepository stationRepository) : ControllerBase
{
    private static string NormalizeCurrencyCode(string? code)
    {
        var normalized = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (!Regex.IsMatch(normalized, "^[A-Z]{3}$"))
            return "USD";
        return normalized;
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

    [HttpGet]
    public async Task<IActionResult> GetPaged(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? q = null,
        [FromQuery] int? filterStationId = null)
    {
        if (IsSuperAdmin(User))
        {
            return Ok(await repository.GetPagedAsync(page, pageSize, q, null, filterStationId));
        }

        if (!TryGetJwtBusiness(out var bid))
        {
            return BadRequest("No business assigned to this user.");
        }

        var stationFilter = ListStationFilter.ForNonSuperAdmin(User, filterStationId);
        return Ok(await repository.GetPagedAsync(page, pageSize, q, bid, stationFilter));
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

        var bad = await ValidateExpenseStationAsync(targetBusinessId, dto.StationId);
        if (bad is not null)
        {
            return bad;
        }

        if (!double.TryParse(dto.LocalAmount.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var local))
        {
            return BadRequest("Invalid local amount.");
        }

        if (!double.TryParse(dto.Rate.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var rate))
        {
            return BadRequest("Invalid rate.");
        }

        if (!double.TryParse(dto.AmountUsd.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var usd))
        {
            return BadRequest("Invalid USD amount.");
        }

        var entity = new Expense
        {
            Date = dto.Date?.UtcDateTime ?? DateTime.UtcNow,
            Description = dto.Description,
            CurrencyCode = NormalizeCurrencyCode(dto.CurrencyCode),
            LocalAmount = local,
            Rate = rate,
            AmountUsd = usd,
            UserId = userId,
            BusinessId = targetBusinessId,
            StationId = dto.StationId,
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

        var bad = await ValidateExpenseStationAsync(targetBusinessId, dto.StationId);
        if (bad is not null)
        {
            return bad;
        }

        if (!double.TryParse(dto.LocalAmount.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var local))
        {
            return BadRequest("Invalid local amount.");
        }

        if (!double.TryParse(dto.Rate.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var rate))
        {
            return BadRequest("Invalid rate.");
        }

        if (!double.TryParse(dto.AmountUsd.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var usd))
        {
            return BadRequest("Invalid USD amount.");
        }

        existing.Description = dto.Description;
        existing.CurrencyCode = NormalizeCurrencyCode(dto.CurrencyCode);
        existing.LocalAmount = local;
        existing.Rate = rate;
        existing.AmountUsd = usd;
        existing.StationId = dto.StationId;
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
