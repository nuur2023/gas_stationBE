using System.Globalization;
using System.Security.Claims;
using backend.Common;
using backend.Data.Context;
using backend.Data.Interfaces;
using backend.Models;
using backend.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace backend.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CustomerFuelGivensController(
    ICustomerFuelGivenRepository repository,
    IStationRepository stationRepository,
    IDippingRepository dippingRepository,
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

    private bool TryGetJwtStation(out int stationId)
    {
        stationId = 0;
        var s = User.FindFirstValue("station_id");
        return !string.IsNullOrEmpty(s) && int.TryParse(s, out stationId);
    }

    private bool ResolveBusiness(CustomerFuelGivenWriteRequestViewModel dto, out int targetBusinessId, out IActionResult? err)
    {
        targetBusinessId = 0;
        err = null;
        if (IsSuperAdmin(User))
        {
            if (dto.BusinessId <= 0)
            {
                err = BadRequest("Select a business.");
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

    private async Task<IActionResult?> ValidateStationAsync(int targetBusinessId, int stationId)
    {
        var st = await stationRepository.GetByIdAsync(stationId);
        if (st is null || st.BusinessId != targetBusinessId)
        {
            return BadRequest("Station does not belong to the selected business.");
        }

        if (TryGetJwtStation(out var js) && js > 0 && stationId != js)
        {
            return BadRequest("You can only record data for your assigned station.");
        }

        return null;
    }

    private async Task<IActionResult?> SubtractFromDippingAsync(int stationId, int fuelTypeId, double givenLiter)
    {
        var dipping = await dippingRepository.GetFirstByStationAndFuelAsync(stationId, fuelTypeId);
        if (dipping is null)
        {
            return BadRequest("No dipping found for this station and fuel type.");
        }

        var next = dipping.AmountLiter - givenLiter;
        if (next < 0)
        {
            return BadRequest("Dipping balance cannot go negative.");
        }

        dipping.AmountLiter = next;
        await dippingRepository.UpdateAsync(dipping.Id, dipping);
        return null;
    }

    private async Task<IActionResult?> RestoreToDippingAsync(int stationId, int fuelTypeId, double givenLiter)
    {
        var dipping = await dippingRepository.GetFirstByStationAndFuelAsync(stationId, fuelTypeId);
        if (dipping is null)
        {
            return BadRequest("No dipping found for this station and fuel type.");
        }

        dipping.AmountLiter += givenLiter;
        await dippingRepository.UpdateAsync(dipping.Id, dipping);
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

    /// <summary>Customer fuel-given rows with a positive outstanding balance (fully paid rows are omitted).</summary>
    [HttpGet("outstanding")]
    public async Task<IActionResult> GetOutstanding(
        [FromQuery] int? filterBusinessId = null,
        [FromQuery] int? filterStationId = null)
    {
        int bid;
        if (IsSuperAdmin(User))
        {
            if (filterBusinessId is not > 0)
                return BadRequest("filterBusinessId is required.");
            bid = filterBusinessId.Value;
        }
        else
        {
            if (!TryGetJwtBusiness(out bid))
                return BadRequest("No business assigned to this user.");
            if (filterBusinessId is > 0 && filterBusinessId.Value != bid)
                return Forbid();
        }

        int? stationFilter = IsSuperAdmin(User)
            ? (filterStationId is > 0 ? filterStationId : null)
            : ListStationFilter.ForNonSuperAdmin(User, filterStationId);

        var givensQuery = dbContext.Set<CustomerFuelGiven>().AsNoTracking().Where(x => !x.IsDeleted && x.BusinessId == bid);
        if (stationFilter is > 0)
            givensQuery = givensQuery.Where(x => x.StationId == stationFilter.Value);

        var givens = await givensQuery
            .OrderByDescending(x => x.Date)
            .ThenByDescending(x => x.Id)
            .ToListAsync();

        if (givens.Count == 0)
            return Ok(Array.Empty<object>());

        var givenIds = givens.Select(g => g.Id).ToList();
        var paidSums = await dbContext.Set<CustomerPayment>()
            .AsNoTracking()
            .Where(p => !p.IsDeleted && givenIds.Contains(p.CustomerFuelGivenId))
            .GroupBy(p => p.CustomerFuelGivenId)
            .Select(g => new { GivenId = g.Key, Total = g.Sum(x => x.AmountPaid) })
            .ToListAsync();
        var paidMap = paidSums.ToDictionary(x => x.GivenId, x => x.Total);

        var rows = new List<object>();
        foreach (var g in givens)
        {
            var due = g.GivenLiter * g.Price;
            var paid = paidMap.GetValueOrDefault(g.Id);
            var balance = Math.Round(due - paid, 2, MidpointRounding.AwayFromZero);
            if (balance <= 0.0001)
                continue;

            rows.Add(new
            {
                g.Id,
                g.Name,
                g.Phone,
                totalDue = Math.Round(due, 2, MidpointRounding.AwayFromZero),
                totalPaid = Math.Round(paid, 2, MidpointRounding.AwayFromZero),
                balance,
                date = g.Date,
                g.StationId,
                g.FuelTypeId,
                givenLiter = g.GivenLiter,
                price = g.Price,
                usdAmount = g.UsdAmount,
            });
        }

        return Ok(rows);
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
    public async Task<IActionResult> Create([FromBody] CustomerFuelGivenWriteRequestViewModel dto)
    {
        if (!ResolveBusiness(dto, out var targetBusinessId, out var bizErr))
        {
            return bizErr!;
        }

        var bad = await ValidateStationAsync(targetBusinessId, dto.StationId);
        if (bad is not null)
        {
            return bad;
        }

        if (!double.TryParse(dto.GivenLiter.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var givenLiter) || givenLiter <= 0)
        {
            return BadRequest("Invalid given liter.");
        }

        if (!double.TryParse(dto.Price.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var price) || price < 0)
        {
            return BadRequest("Invalid price.");
        }
        if (!double.TryParse(dto.AmountUsd.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var amountUsd) || amountUsd < 0)
        {
            return BadRequest("Invalid amount USD.");
        }

        var entity = new CustomerFuelGiven
        {
            Name = dto.Name.Trim(),
            Phone = dto.Phone.Trim(),
            FuelTypeId = dto.FuelTypeId,
            GivenLiter = givenLiter,
            Price = price,
            UsdAmount = amountUsd,
            Remark = string.IsNullOrWhiteSpace(dto.Remark) ? null : dto.Remark.Trim(),
            StationId = dto.StationId,
            BusinessId = targetBusinessId,
            Date = dto.Date?.UtcDateTime ?? DateTime.UtcNow,
        };

        await using var tx = await dbContext.Database.BeginTransactionAsync();
        try
        {
            var dipErr = await SubtractFromDippingAsync(entity.StationId, entity.FuelTypeId, entity.GivenLiter);
            if (dipErr is not null)
            {
                await tx.RollbackAsync();
                return dipErr;
            }

            var added = await repository.AddAsync(entity);

            await tx.CommitAsync();
            return Ok(added);
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] CustomerFuelGivenWriteRequestViewModel dto)
    {
        if (!ResolveBusiness(dto, out var targetBusinessId, out var bizErr))
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
                return BadRequest("Record belongs to a different business.");
            }
        }
        else if (!TryGetJwtBusiness(out var bid) || existing.BusinessId != bid)
        {
            return Forbid();
        }

        var bad = await ValidateStationAsync(targetBusinessId, dto.StationId);
        if (bad is not null)
        {
            return bad;
        }

        if (!double.TryParse(dto.GivenLiter.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var givenLiter) || givenLiter <= 0)
        {
            return BadRequest("Invalid given liter.");
        }

        if (!double.TryParse(dto.Price.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var price) || price < 0)
        {
            return BadRequest("Invalid price.");
        }
        if (!double.TryParse(dto.AmountUsd.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var amountUsd) || amountUsd < 0)
        {
            return BadRequest("Invalid amount USD.");
        }

        await using var tx = await dbContext.Database.BeginTransactionAsync();
        try
        {
            var restoreErr = await RestoreToDippingAsync(existing.StationId, existing.FuelTypeId, existing.GivenLiter);
            if (restoreErr is not null)
            {
                await tx.RollbackAsync();
                return restoreErr;
            }

            var dipErr = await SubtractFromDippingAsync(dto.StationId, dto.FuelTypeId, givenLiter);
            if (dipErr is not null)
            {
                await tx.RollbackAsync();
                return dipErr;
            }

            existing.Name = dto.Name.Trim();
            existing.Phone = dto.Phone.Trim();
            existing.FuelTypeId = dto.FuelTypeId;
            existing.GivenLiter = givenLiter;
            existing.Price = price;
            existing.UsdAmount = amountUsd;
            existing.Remark = string.IsNullOrWhiteSpace(dto.Remark) ? null : dto.Remark.Trim();
            existing.StationId = dto.StationId;
            existing.Date = dto.Date?.UtcDateTime ?? existing.Date;

            var updated = await repository.UpdateAsync(id, existing);
            await tx.CommitAsync();
            return Ok(updated);
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
        var existing = await repository.GetByIdAsync(id);
        if (existing is null)
        {
            return NotFound();
        }

        if (!IsSuperAdmin(User))
        {
            if (!TryGetJwtBusiness(out var bid) || existing.BusinessId != bid)
            {
                return Forbid();
            }
        }

        var deleted = await repository.DeleteAsync(id);
        return Ok(deleted);
    }
}

