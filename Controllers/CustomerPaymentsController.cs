using System.Globalization;
using System.Security.Claims;
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
    ICustomerFuelGivenRepository customerFuelGivenRepository,
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
    public async Task<IActionResult> GetPaged([FromQuery] int page = 1, [FromQuery] int pageSize = 50, [FromQuery] string? q = null)
    {
        if (IsSuperAdmin(User))
            return Ok(await repository.GetPagedAsync(page, pageSize, q, null));

        if (!TryGetJwtBusiness(out var bid))
            return BadRequest("No business assigned to this user.");
        return Ok(await repository.GetPagedAsync(page, pageSize, q, bid));
    }

    /// <summary>Outstanding balance for a customer fuel-given row (before recording a new payment).</summary>
    [HttpGet("preview-balance")]
    public async Task<IActionResult> PreviewBalance([FromQuery] int customerFuelGivenId, [FromQuery] int? businessId = null)
    {
        if (customerFuelGivenId <= 0)
            return BadRequest("customerFuelGivenId is required.");

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

        var cfg = await customerFuelGivenRepository.GetByIdAsync(customerFuelGivenId);
        if (cfg is null || cfg.BusinessId != bid)
            return NotFound();

        var alreadyPaid = await dbContext.CustomerPayments
            .Where(x => !x.IsDeleted && x.CustomerFuelGivenId == cfg.Id)
            .SumAsync(x => (double?)x.AmountPaid) ?? 0;
        var totalDue = cfg.GivenLiter * cfg.Price;
        var balance = Math.Round(Math.Max(0, totalDue - alreadyPaid), 2, MidpointRounding.AwayFromZero);

        return Ok(new
        {
            name = cfg.Name,
            phone = cfg.Phone,
            totalDue = Math.Round(totalDue, 2, MidpointRounding.AwayFromZero),
            totalPaid = Math.Round(alreadyPaid, 2, MidpointRounding.AwayFromZero),
            balance,
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CustomerPaymentWriteRequestViewModel dto)
    {
        if (!ResolveBusiness(dto.BusinessId, out var bid, out var err)) return err!;
        if (!TryGetUserId(out var userId, out var uerr)) return uerr!;

        if (!double.TryParse(dto.AmountPaid.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var paid) || paid <= 0)
            return BadRequest("Invalid paid amount.");

        var cfg = await customerFuelGivenRepository.GetByIdAsync(dto.CustomerFuelGivenId);
        if (cfg is null || cfg.BusinessId != bid) return BadRequest("Customer fuel-given not found in this business.");

        var alreadyPaid = await dbContext.CustomerPayments
            .Where(x => !x.IsDeleted && x.CustomerFuelGivenId == cfg.Id)
            .SumAsync(x => (double?)x.AmountPaid) ?? 0;
        var receivable = (cfg.GivenLiter * cfg.Price) - alreadyPaid;
        if (paid > receivable + 0.0001) return BadRequest("Amount paid exceeds customer outstanding balance.");

        var row = new CustomerPayment
        {
            CustomerFuelGivenId = cfg.Id,
            AmountPaid = paid,
            PaymentDate = dto.PaymentDate?.UtcDateTime ?? DateTime.UtcNow,
            BusinessId = bid,
            UserId = userId,
        };

        var added = await repository.AddAsync(row);
        return Ok(added);
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

