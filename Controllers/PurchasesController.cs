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
public class PurchasesController(
    IPurchaseRepository repository,
    ISupplierRepository supplierRepository,
    IFuelTypeRepository fuelTypeRepository) : ControllerBase
{
    private const string SuperAdminRole = "SuperAdmin";

    private static bool IsSuperAdmin(ClaimsPrincipal user) => user.IsInRole(SuperAdminRole);

    private bool TryGetJwtBusiness(out int businessId)
    {
        businessId = 0;
        var bidStr = User.FindFirstValue("business_id");
        return !string.IsNullOrEmpty(bidStr) && int.TryParse(bidStr, out businessId);
    }

    private bool ResolvePurchaseBusiness(PurchaseWriteRequestViewModel dto, out int targetBusinessId, out IActionResult? err)
    {
        targetBusinessId = 0;
        err = null;
        if (IsSuperAdmin(User))
        {
            if (dto.BusinessId <= 0)
            {
                err = BadRequest("Select a business for this purchase.");
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

    private bool ResolvePurchaseBusiness(PurchaseHeaderWriteRequestViewModel dto, out int targetBusinessId, out IActionResult? err)
    {
        targetBusinessId = 0;
        err = null;
        if (IsSuperAdmin(User))
        {
            if (dto.BusinessId <= 0)
            {
                err = BadRequest("Select a business for this purchase.");
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

    private async Task<IActionResult?> ValidateSupplierForBusinessAsync(int supplierId, int businessId)
    {
        var sup = await supplierRepository.GetByIdAsync(supplierId);
        if (sup is null || sup.BusinessId != businessId)
        {
            return BadRequest("Supplier not found or does not belong to this business.");
        }

        return null;
    }

    private async Task<(IActionResult? Error, PurchaseItem? Item)> ParseOneItemAsync(PurchaseItemWriteRequestViewModel line)
    {
        if (line.FuelTypeId <= 0)
        {
            return (BadRequest("Each line must have a valid fuel type."), null);
        }

        var ft = await fuelTypeRepository.GetByIdAsync(line.FuelTypeId);
        if (ft is null)
        {
            return (BadRequest($"Fuel type {line.FuelTypeId} not found."), null);
        }

        if (!double.TryParse(line.Liters.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var liters) ||
            liters <= 0)
        {
            return (BadRequest("Invalid liters on a line item."), null);
        }

        if (!double.TryParse(line.PricePerLiter.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var ppl) ||
            ppl < 0)
        {
            return (BadRequest("Invalid price per liter on a line item."), null);
        }

        var computed = Math.Round(liters * ppl, 2, MidpointRounding.AwayFromZero);
        if (!double.TryParse(line.TotalAmount.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var total) ||
            total < 0)
        {
            total = computed;
        }

        var item = new PurchaseItem
        {
            FuelTypeId = line.FuelTypeId,
            Liters = liters,
            PricePerLiter = ppl,
            TotalAmount = Math.Abs(total - computed) < 0.02 ? total : computed,
        };
        return (null, item);
    }

    private static string NormalizePurchaseStatus(string? raw)
    {
        var s = (raw ?? "").Trim().ToLowerInvariant();
        return s switch
        {
            "paid" => "Paid",
            "half-paid" => "Half-paid",
            "half paid" => "Half-paid",
            _ => "Unpaid",
        };
    }

    private async Task<(IActionResult? Error, List<PurchaseItem> Items)> ParseItemsAsync(
        List<PurchaseItemWriteRequestViewModel> dtos)
    {
        var list = new List<PurchaseItem>(dtos.Count);
        foreach (var line in dtos)
        {
            var (err, item) = await ParseOneItemAsync(line);
            if (err is not null)
            {
                return (err, list);
            }

            list.Add(item!);
        }

        return (null, list);
    }

    private async Task<IActionResult?> EnsurePurchaseAccessAsync(int id)
    {
        var detail = await repository.GetDetailAsync(id);
        if (detail is null)
        {
            return NotFound();
        }

        if (!IsSuperAdmin(User))
        {
            if (!TryGetJwtBusiness(out var bid) || detail.BusinessId != bid)
            {
                return NotFound();
            }
        }

        return null;
    }

    [HttpGet]
    public async Task<IActionResult> GetPaged([FromQuery] int page = 1, [FromQuery] int pageSize = 50, [FromQuery] string? q = null)
    {
        if (IsSuperAdmin(User))
        {
            return Ok(await repository.GetPagedAsync(page, pageSize, q, null));
        }

        if (!TryGetJwtBusiness(out var bid))
        {
            return BadRequest("No business assigned to this user.");
        }

        return Ok(await repository.GetPagedAsync(page, pageSize, q, bid));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var detail = await repository.GetDetailAsync(id);
        if (detail is null)
        {
            return NotFound();
        }

        if (!IsSuperAdmin(User))
        {
            if (!TryGetJwtBusiness(out var bid) || detail.BusinessId != bid)
            {
                return NotFound();
            }
        }

        return Ok(detail);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] PurchaseWriteRequestViewModel dto)
    {
        if (!ResolvePurchaseBusiness(dto, out var targetBusinessId, out var bizErr))
        {
            return bizErr!;
        }

        var supErr = await ValidateSupplierForBusinessAsync(dto.SupplierId, targetBusinessId);
        if (supErr is not null)
        {
            return supErr;
        }

        List<PurchaseItem> items = [];
        if (dto.Items is { Count: > 0 })
        {
            var (parseErr, parsed) = await ParseItemsAsync(dto.Items);
            if (parseErr is not null)
            {
                return parseErr;
            }

            items = parsed;
        }

        var purchase = new Purchase
        {
            SupplierId = dto.SupplierId,
            InvoiceNo = dto.InvoiceNo.Trim(),
            BusinessId = targetBusinessId,
            PurchaseDate = dto.PurchaseDate?.UtcDateTime ?? DateTime.UtcNow,
            Status = NormalizePurchaseStatus(dto.Status),
            AmountPaid = double.TryParse((dto.AmountPaid ?? "0").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var ap) ? Math.Max(0, ap) : 0,
        };

        try
        {
            var created = await repository.AddWithItemsAsync(purchase, items);
            return Ok(created);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] PurchaseHeaderWriteRequestViewModel dto)
    {
        if (!ResolvePurchaseBusiness(dto, out var targetBusinessId, out var bizErr))
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
                return BadRequest("Purchase belongs to a different business.");
            }
        }
        else if (!TryGetJwtBusiness(out var bid) || existing.BusinessId != bid)
        {
            return NotFound();
        }

        var supErr = await ValidateSupplierForBusinessAsync(dto.SupplierId, targetBusinessId);
        if (supErr is not null)
        {
            return supErr;
        }

        var purchase = new Purchase
        {
            SupplierId = dto.SupplierId,
            InvoiceNo = dto.InvoiceNo.Trim(),
            BusinessId = targetBusinessId,
            PurchaseDate = dto.PurchaseDate?.UtcDateTime ?? existing.PurchaseDate,
            Status = NormalizePurchaseStatus(dto.Status),
            AmountPaid = double.TryParse((dto.AmountPaid ?? "0").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var ap) ? Math.Max(0, ap) : 0,
        };

        var updated = await repository.UpdateHeaderAsync(id, purchase);
        if (updated is null)
        {
            return NotFound();
        }

        return Ok(updated);
    }

    [HttpPost("{id:int}/items")]
    public async Task<IActionResult> AddItem(int id, [FromBody] PurchaseItemWriteRequestViewModel line)
    {
        var access = await EnsurePurchaseAccessAsync(id);
        if (access is not null)
        {
            return access;
        }

        var (parseErr, item) = await ParseOneItemAsync(line);
        if (parseErr is not null)
        {
            return parseErr;
        }

        var detail = await repository.AddItemAsync(id, item!);
        return detail is null ? NotFound() : Ok(detail);
    }

    [HttpPut("{id:int}/items/{itemId:int}")]
    public async Task<IActionResult> UpdateItem(int id, int itemId, [FromBody] PurchaseItemWriteRequestViewModel line)
    {
        var access = await EnsurePurchaseAccessAsync(id);
        if (access is not null)
        {
            return access;
        }

        var (parseErr, item) = await ParseOneItemAsync(line);
        if (parseErr is not null)
        {
            return parseErr;
        }

        var detail = await repository.UpdateItemAsync(id, itemId, item!);
        return detail is null ? NotFound() : Ok(detail);
    }

    [HttpDelete("{id:int}/items/{itemId:int}")]
    public async Task<IActionResult> DeleteItem(int id, int itemId)
    {
        var access = await EnsurePurchaseAccessAsync(id);
        if (access is not null)
        {
            return access;
        }

        var detail = await repository.DeleteItemAsync(id, itemId);
        return detail is null ? NotFound() : Ok(detail);
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
                return NotFound();
            }
        }

        try
        {
            await repository.DeleteAsync(id);
            return Ok();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }
}
