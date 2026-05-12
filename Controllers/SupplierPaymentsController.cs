using System.Globalization;
using System.Security.Claims;
using gas_station.Data.Interfaces;
using gas_station.Models;
using gas_station.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace gas_station.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SupplierPaymentsController(
    ISupplierPaymentRepository repository,
    ISupplierRepository supplierRepository) : ControllerBase
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

    private bool ResolveBusiness(SupplierPaymentWriteRequestViewModel dto, out int targetBusinessId, out IActionResult? err)
    {
        targetBusinessId = 0;
        err = null;
        if (IsSuperAdmin(User))
        {
            if (dto.BusinessId <= 0)
            {
                err = BadRequest("Select a business for this payment.");
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

    private bool ResolveBusinessForRead(int? requested, out int bid, out IActionResult? err)
    {
        bid = 0;
        err = null;
        if (IsSuperAdmin(User))
        {
            if (requested is > 0)
            {
                bid = requested.Value;
                return true;
            }

            err = BadRequest("businessId is required.");
            return false;
        }

        if (!TryGetJwtBusiness(out var jwtBid))
        {
            err = BadRequest("No business assigned to this user.");
            return false;
        }

        if (requested is > 0 && requested.Value != jwtBid)
        {
            err = Forbid();
            return false;
        }

        bid = jwtBid;
        return true;
    }

    [HttpGet]
    public async Task<IActionResult> GetPaged(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? q = null,
        [FromQuery] int? businessId = null)
    {
        if (IsSuperAdmin(User))
            return Ok(await repository.GetPagedAsync(page, pageSize, q, businessId));

        if (!TryGetJwtBusiness(out var bid))
            return BadRequest("No business assigned to this user.");

        return Ok(await repository.GetPagedAsync(page, pageSize, q, bid));
    }

    /// <summary>
    /// Current outstanding balance for the supplier (charged − paid). Used by the SupplierPayments form
    /// preview card and the Supplier Report's footer.
    /// </summary>
    [HttpGet("balance")]
    public async Task<IActionResult> GetBalance([FromQuery] int supplierId, [FromQuery] int? businessId = null)
    {
        if (supplierId <= 0)
            return BadRequest("supplierId is required.");

        if (!ResolveBusinessForRead(businessId, out var bid, out var err))
            return err!;

        var sup = await supplierRepository.GetByIdAsync(supplierId);
        if (sup is null || sup.BusinessId != bid)
            return BadRequest("Supplier not found or does not belong to this business.");

        var bal = await repository.GetSupplierBalanceAsync(bid, supplierId);
        return Ok(new { supplierId, businessId = bid, balance = bal });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SupplierPaymentWriteRequestViewModel dto)
    {
        if (!TryGetUserId(out var userId, out var uerr))
            return uerr!;

        if (!ResolveBusiness(dto, out var targetBusinessId, out var bizErr))
            return bizErr!;

        if (dto.SupplierId <= 0)
            return BadRequest("Supplier is required.");

        var sup = await supplierRepository.GetByIdAsync(dto.SupplierId);
        if (sup is null || sup.BusinessId != targetBusinessId)
            return BadRequest("Supplier not found or does not belong to this business.");

        if (!double.TryParse((dto.Amount ?? "0").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var amt) ||
            amt <= 0)
            return BadRequest("Amount must be greater than zero.");

        var paymentDate = dto.Date?.UtcDateTime ?? DateTime.UtcNow;
        var paid = Math.Round(amt, 2, MidpointRounding.AwayFromZero);
        var refNo = await repository.GenerateReferenceAsync(targetBusinessId, paymentDate);

        var entity = new SupplierPayment
        {
            ReferenceNo = refNo,
            SupplierId = dto.SupplierId,
            Description = "Payment",
            ChargedAmount = 0,
            PaidAmount = paid,
            Balance = 0,
            PurchaseId = null,
            Date = paymentDate,
            BusinessId = targetBusinessId,
            UserId = userId,
        };

        var saved = await repository.AddAsync(entity);
        await repository.RecalculateSupplierBalancesAsync(targetBusinessId, dto.SupplierId);
        return Ok(saved);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] SupplierPaymentUpdateRequestViewModel dto)
    {
        if (!TryGetUserId(out _, out var uerr))
            return uerr!;

        if (!ResolveBusiness(
                new SupplierPaymentWriteRequestViewModel
                {
                    BusinessId = dto.BusinessId,
                    SupplierId = 0,
                    Amount = dto.Amount,
                    Date = dto.Date,
                },
                out var targetBusinessId,
                out var bizErr))
            return bizErr!;

        var existing = await repository.GetByIdAsync(id);
        if (existing is null)
            return NotFound();

        if (existing.BusinessId != targetBusinessId)
            return Forbid();

        if (!double.TryParse((dto.Amount ?? "0").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var amt) ||
            amt <= 0)
            return BadRequest("Amount must be greater than zero.");

        var paymentDate = dto.Date?.UtcDateTime ?? existing.Date;
        var paid = Math.Round(amt, 2, MidpointRounding.AwayFromZero);

        var ok = await repository.TryUpdateManualPaymentAsync(id, paid, paymentDate);
        if (!ok)
            return BadRequest("Only manual payment rows can be updated.");

        var updated = await repository.GetByIdAsync(id);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (!TryGetUserId(out _, out var uerr))
            return uerr!;

        var existing = await repository.GetByIdAsync(id);
        if (existing is null)
            return NotFound();

        if (!IsSuperAdmin(User))
        {
            if (!TryGetJwtBusiness(out var bid) || existing.BusinessId != bid)
                return Forbid();
        }

        var ok = await repository.TryDeleteManualPaymentAsync(id);
        if (!ok)
            return BadRequest("Only manual payment rows can be deleted.");

        return Ok();
    }
}
