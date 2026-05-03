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
        var refNo = string.IsNullOrWhiteSpace(dto.ReferenceNo) ? null : dto.ReferenceNo.Trim();

        var entity = new SupplierPayment
        {
            ReferenceNo = refNo,
            SupplierId = dto.SupplierId,
            Amount = Math.Round(amt, 2, MidpointRounding.AwayFromZero),
            Date = paymentDate,
            BusinessId = targetBusinessId,
            UserId = userId,
        };

        return Ok(await repository.AddAsync(entity));
    }
}
