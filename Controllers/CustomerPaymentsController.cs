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
public class CustomerPaymentsController(
    ICustomerPaymentRepository repository,
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
        var stationScope = ListStationFilter.ForNonSuperAdmin(User, filterStationId);
        return Ok(await repository.GetPagedAsync(page, pageSize, q, bid, stationScope));
    }

    /// <summary>Outstanding balance for a customer (before recording a new payment).</summary>
    [HttpGet("preview-balance")]
    public async Task<IActionResult> PreviewBalance([FromQuery] int customerId, [FromQuery] int? businessId = null)
    {
        if (customerId <= 0)
            return BadRequest("customerId is required.");

        int bid;
        if (IsSuperAdmin(User))
        {
            if (businessId is not > 0)
                return BadRequest("businessId is required.");
            bid = businessId.Value;
        }
        else
        {
            if (!TryGetJwtBusiness(out bid))
                return BadRequest("No business assigned to this user.");
            if (businessId is > 0 && businessId.Value != bid)
                return Forbid();
        }

        var customer = await dbContext.Customers.AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == customerId && x.BusinessId == bid);
        if (customer is null)
            return NotFound();

        var balance = await repository.GetCustomerBalanceAsync(bid, customerId);
        var rows = await dbContext.CustomerPayments.AsNoTracking()
            .Where(x => !x.IsDeleted
                        && x.BusinessId == bid
                        && x.CustomerId == customerId)
            .Select(x => new { x.ChargedAmount, x.AmountPaid })
            .ToListAsync();
        var totalDue = rows.Sum(x => x.ChargedAmount);
        var totalPaid = rows.Sum(x => x.AmountPaid);

        return Ok(new
        {
            name = customer.Name,
            phone = customer.Phone,
            totalDue = Math.Round(totalDue, 2, MidpointRounding.AwayFromZero),
            totalPaid = Math.Round(totalPaid, 2, MidpointRounding.AwayFromZero),
            balance = Math.Round(Math.Max(0, balance), 2, MidpointRounding.AwayFromZero),
        });
    }

    /// <summary>Outstanding balance for a customer within a business.</summary>
    [HttpGet("balance")]
    public async Task<IActionResult> CustomerBalance(
        [FromQuery] int customerId,
        [FromQuery] int? businessId = null)
    {
        if (customerId <= 0) return BadRequest("customerId is required.");

        int bid;
        if (IsSuperAdmin(User))
        {
            if (businessId is not > 0)
                return BadRequest("businessId is required.");
            bid = businessId.Value;
        }
        else
        {
            if (!TryGetJwtBusiness(out bid))
                return BadRequest("No business assigned to this user.");
            if (businessId is > 0 && businessId.Value != bid)
                return Forbid();
        }

        var customer = await dbContext.Customers.AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == customerId && x.BusinessId == bid);
        if (customer is null) return NotFound();
        var bal = await repository.GetCustomerBalanceAsync(bid, customerId);
        return Ok(new { businessId = bid, customerId, name = customer.Name, phone = customer.Phone, balance = bal });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CustomerPaymentWriteRequestViewModel dto)
    {
        if (!ResolveBusiness(dto.BusinessId, out var bid, out var err)) return err!;
        if (!TryGetUserId(out var userId, out var uerr)) return uerr!;

        if (!double.TryParse((dto.AmountPaid ?? "0").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var paid) || paid <= 0)
            return BadRequest("Invalid paid amount.");

        if (dto.CustomerId <= 0) return BadRequest("customerId is required.");
        var customer = await dbContext.Customers.AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == dto.CustomerId && x.BusinessId == bid);
        if (customer is null) return BadRequest("Customer not found in this business.");

        var paymentDate = dto.PaymentDate?.UtcDateTime ?? DateTime.UtcNow;
        var refNo = await repository.GenerateReferenceAsync(bid, paymentDate);

        var row = new CustomerPayment
        {
            CustomerId = dto.CustomerId,
            ReferenceNo = refNo,
            Description = "Payment",
            ChargedAmount = 0,
            AmountPaid = Math.Round(paid, 2, MidpointRounding.AwayFromZero),
            Balance = 0,
            PaymentDate = paymentDate,
            BusinessId = bid,
            UserId = userId,
        };

        var added = await repository.AddAsync(row);
        await repository.RecalculateCustomerBalancesAsync(bid, dto.CustomerId);
        return Ok(added);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] CustomerPaymentUpdateRequestViewModel dto)
    {
        if (!ResolveBusiness(dto.BusinessId, out var bid, out var err))
        {
            return err!;
        }

        if (!TryGetUserId(out var userId, out var uerr))
        {
            return uerr!;
        }

        var row = await repository.GetByIdAsync(id);
        if (row is null)
        {
            return NotFound();
        }

        if (row.BusinessId != bid)
        {
            return Forbid();
        }

        if (!string.Equals(row.Description, "Payment", StringComparison.OrdinalIgnoreCase)
            || row.ChargedAmount > 0.0001)
        {
            return BadRequest("Only manual payment rows can be edited.");
        }

        if (row.AmountPaid <= 0.0001)
        {
            return BadRequest("This ledger row cannot be edited.");
        }

        if (!double.TryParse((dto.AmountPaid ?? "0").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var paid)
            || paid <= 0)
        {
            return BadRequest("Invalid paid amount.");
        }

        row.AmountPaid = Math.Round(paid, 2, MidpointRounding.AwayFromZero);
        row.PaymentDate = dto.PaymentDate?.UtcDateTime ?? row.PaymentDate;
        row.UserId = userId;

        var updated = await repository.UpdateAsync(id, row);
        await repository.RecalculateCustomerBalancesAsync(row.BusinessId, row.CustomerId);
        return Ok(updated);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var row = await repository.GetByIdAsync(id);
        if (row is null) return NotFound();
        if (!IsSuperAdmin(User) && (!TryGetJwtBusiness(out var bid) || row.BusinessId != bid)) return Forbid();
        var deleted = await repository.DeleteAsync(id);
        await repository.RecalculateCustomerBalancesAsync(row.BusinessId, row.CustomerId);
        return Ok(deleted);
    }
}
