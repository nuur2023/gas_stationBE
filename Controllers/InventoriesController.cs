using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using gas_station.Common;
using gas_station.Data.Context;
using gas_station.Data.Interfaces;
using gas_station.Models;
using gas_station.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace gas_station.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class InventoriesController(
    IInventoryRepository repository,
    IPumpRepository pumpRepository,
    INozzleRepository nozzleRepository,
    IDippingPumpRepository dippingPumpRepository,
    IStationRepository stationRepository,
    IDippingRepository dippingRepository,
    GasStationDBContext dbContext,
    IWebHostEnvironment webHostEnvironment) : ControllerBase
{
    private const string SuperAdminRole = "SuperAdmin";

    private static bool IsSuperAdmin(ClaimsPrincipal user) =>
        user.IsInRole(SuperAdminRole);

    /// <summary>Treat null/whitespace liter fields as zero so clients can omit or send empty strings.</summary>
    private static string NormalizeInventoryLiterString(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "0" : value.Trim();

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

    private bool ResolveInventoryBusiness(InventoryWriteRequestViewModel dto, out int targetBusinessId, out IActionResult? error)
    {
        targetBusinessId = 0;
        error = null;

        if (IsSuperAdmin(User))
        {
            if (dto.BusinessId <= 0)
            {
                error = BadRequest("Select a business for this inventory.");
                return false;
            }

            targetBusinessId = dto.BusinessId;
            return true;
        }

        if (!TryGetJwtBusiness(out var jwtBid))
        {
            error = BadRequest("No business assigned to this user.");
            return false;
        }

        if (dto.BusinessId > 0 && dto.BusinessId != jwtBid)
        {
            error = Forbid();
            return false;
        }

        targetBusinessId = jwtBid;
        return true;
    }

    private async Task<IActionResult?> ValidateStationAndNozzleAsync(int targetBusinessId, int stationId, int nozzleId)
    {
        var station = await stationRepository.GetByIdAsync(stationId);
        if (station is null || station.BusinessId != targetBusinessId)
        {
            return BadRequest("Station does not belong to the selected business.");
        }

        if (TryGetJwtStation(out var jwtSid) && jwtSid > 0 && stationId != jwtSid)
        {
            return BadRequest("You can only record inventory for your assigned station.");
        }

        var nozzle = await nozzleRepository.GetByIdAsync(nozzleId);
        if (nozzle is null || nozzle.BusinessId != targetBusinessId || nozzle.StationId != stationId)
        {
            return BadRequest("Nozzle does not match the selected business and station.");
        }

        var pump = await pumpRepository.GetByIdAsync(nozzle.PumpId);
        if (pump is null || pump.BusinessId != targetBusinessId || pump.StationId != stationId)
        {
            return BadRequest("Pump for this nozzle does not match the selected business and station.");
        }

        return null;
    }

    /// <summary>Put back usage liters into the tank (dipping) — for update/delete revert.</summary>
    private async Task<IActionResult?> RestoreDippingFromInventoryUsageAsync(int nozzleId, double usageLiters)
    {
        var dipId = await dippingPumpRepository.GetDippingIdByNozzleIdAsync(nozzleId);
        if (!dipId.HasValue)
        {
            return BadRequest("No dipping link for this nozzle.");
        }

        var dip = await dippingRepository.GetByIdAsync(dipId.Value);
        if (dip is null)
        {
            return BadRequest("No dipping (tank) record for this nozzle.");
        }

        dip.AmountLiter += usageLiters;
        await dippingRepository.UpdateAsync(dip.Id, dip);
        return null;
    }

    /// <summary>Remove usage liters from the tank balance (fuel consumed at the pump).</summary>
    private async Task<IActionResult?> SubtractInventoryUsageFromDippingAsync(int nozzleId, double usageLiters)
    {
        var dipId = await dippingPumpRepository.GetDippingIdByNozzleIdAsync(nozzleId);
        if (!dipId.HasValue)
        {
            return BadRequest("No dipping (tank) link for this nozzle. Link a dipping to the nozzle first.");
        }

        var dip = await dippingRepository.GetByIdAsync(dipId.Value);
        if (dip is null)
        {
            return BadRequest("No dipping (tank) record for this nozzle.");
        }

        var next = dip.AmountLiter - usageLiters;
        if (next < 0)
        {
            return BadRequest(
                "Dipping balance cannot go negative after this usage. Reduce usage liters or increase the dipping amount.");
        }

        dip.AmountLiter = next;
        await dippingRepository.UpdateAsync(dip.Id, dip);
        return null;
    }

    private async Task AttachInventoryUserNamesAsync(IReadOnlyList<InventoryResponseDto> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        var ids = items.Select(x => x.UserId).Where(id => id > 0).Distinct().ToList();
        if (ids.Count == 0)
        {
            return;
        }

        var pairs = await dbContext.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id) && !u.IsDeleted)
            .Select(u => new { u.Id, u.Name })
            .ToListAsync();
        var map = pairs.ToDictionary(x => x.Id, x => x.Name);
        foreach (var row in items)
        {
            if (map.TryGetValue(row.UserId, out var name))
            {
                row.UserName = name;
            }
        }
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

    private const double LiterSplitTolerance = 0.001;

    private static double RoundMoney2(double v) =>
        Math.Round(v, 2, MidpointRounding.AwayFromZero);

    private static bool LiterSplitsEqualUsage(double usageLiters, double sspLiters, double usdLiters)
    {
        if (double.IsNaN(usageLiters) || double.IsNaN(sspLiters) || double.IsNaN(usdLiters) ||
            double.IsInfinity(usageLiters) || double.IsInfinity(sspLiters) || double.IsInfinity(usdLiters))
        {
            return false;
        }

        if (sspLiters < -1e-9 || usdLiters < -1e-9)
        {
            return false;
        }

        var sum = sspLiters + usdLiters;
        var tol = LiterSplitTolerance + 1e-9 * Math.Max(Math.Abs(usageLiters), Math.Abs(sum));
        return Math.Abs(sum - usageLiters) <= tol;
    }

    /// <summary>Latest active exchange rate for the business (SSP per USD) — stored on inventory for reporting only.</summary>
    private async Task<double?> GetActiveExchangeRateAsync(int businessId)
    {
        var r = await dbContext.Rates.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BusinessId == businessId && x.Active)
            .OrderByDescending(x => x.Date)
            .ThenByDescending(x => x.Id)
            .Select(x => x.RateNumber)
            .FirstOrDefaultAsync();

        if (r <= 0 || double.IsNaN(r) || double.IsInfinity(r))
        {
            return null;
        }

        return r;
    }

    private async Task<(int FuelTypeId, IActionResult? Err)> ResolveFuelTypeIdForNozzleAsync(int nozzleId)
    {
        var dipId = await dippingPumpRepository.GetDippingIdByNozzleIdAsync(nozzleId);
        if (!dipId.HasValue)
        {
            return (0, BadRequest("No dipping record linked to this nozzle."));
        }

        var dip = await dippingRepository.GetByIdAsync(dipId.Value);
        if (dip is null)
        {
            return (0, BadRequest("No dipping record for this nozzle."));
        }

        return (dip.FuelTypeId, null);
    }

    /// <summary>Latest configured SSP/l and USD/l for this station + fuel type (by currency code).</summary>
    private async Task<(double? SspPerLiter, double? UsdPerLiter, IActionResult? Err)> ResolveSspUsdFuelPricesPerLiterAsync(
        int businessId,
        int stationId,
        int fuelTypeId)
    {
        var rows = await dbContext.FuelPrices
            .AsNoTracking()
            .Include(x => x.Currency)
            .Where(x => !x.IsDeleted && x.BusinessId == businessId && x.StationId == stationId && x.FuelTypeId == fuelTypeId)
            .OrderByDescending(x => x.UpdatedAt)
            .ThenByDescending(x => x.Id)
            .ToListAsync();

        double? ssp = null;
        double? usd = null;
        foreach (var fp in rows)
        {
            var code = fp.Currency?.Code?.Trim().ToUpperInvariant() ?? string.Empty;
            if (usd is null && code == "USD" && fp.Price > 0 && !double.IsNaN(fp.Price) && !double.IsInfinity(fp.Price))
            {
                usd = fp.Price;
            }

            if (ssp is null && code == "SSP" && fp.Price > 0 && !double.IsNaN(fp.Price) && !double.IsInfinity(fp.Price))
            {
                ssp = fp.Price;
            }

            if (ssp is not null && usd is not null)
            {
                break;
            }
        }

        return (ssp, usd, null);
    }

    private async Task<(double SspAmount, double UsdAmount, double SspFuelPrice, double UsdFuelPrice, double ExchangeRate, IActionResult? Err)> ComputeInventoryAmountsAsync(
        int businessId,
        int stationId,
        int fuelTypeId,
        double sspLiters,
        double usdLiters)
    {
        var (sspPer, usdPer, priceErr) = await ResolveSspUsdFuelPricesPerLiterAsync(businessId, stationId, fuelTypeId);
        if (priceErr is not null)
        {
            return (0, 0, 0, 0, 0, priceErr);
        }

        if (sspLiters > 1e-9)
        {
            if (sspPer is null || sspPer.Value <= 0 || double.IsNaN(sspPer.Value) || double.IsInfinity(sspPer.Value))
            {
                return (0, 0, 0, 0, 0, BadRequest("SSP liters are set but no SSP fuel price (currency code SSP) exists for this station and fuel type."));
            }
        }

        if (usdLiters > 1e-9)
        {
            if (usdPer is null || usdPer.Value <= 0 || double.IsNaN(usdPer.Value) || double.IsInfinity(usdPer.Value))
            {
                return (0, 0, 0, 0, 0, BadRequest("USD liters are set but no USD fuel price (currency code USD) exists for this station and fuel type."));
            }
        }

        var sspAmt = sspLiters <= 1e-9 ? 0 : RoundMoney2(sspLiters * sspPer!.Value);
        var usdAmt = usdLiters <= 1e-9 ? 0 : RoundMoney2(usdLiters * usdPer!.Value);
        var sspPrice = sspPer is { } sp && sp > 0 ? RoundMoney2(sp) : 0d;
        var usdPrice = usdPer is { } up && up > 0 ? RoundMoney2(up) : 0d;
        var rate = await GetActiveExchangeRateAsync(businessId);
        var exchangeStored = rate is { } rr && rr > 0 ? RoundMoney2(rr) : 0d;
        return (sspAmt, usdAmt, sspPrice, usdPrice, exchangeStored, null);
    }

    private async Task<string> GenerateReferenceNumberAsync(int businessId, int stationId, DateTime recordedDateUtc)
    {
        _ = recordedDateUtc; // Format no longer uses date by request.

        // Format example: 3A6C4D611
        // - random uppercase hex block
        // - last 2 digits: [businessId][stationId] (single-digit each)
        var bidDigit = Math.Abs(businessId % 10);
        var sidDigit = Math.Abs(stationId % 10);
        var suffix = $"{bidDigit}{sidDigit}";

        for (var i = 0; i < 30; i++)
        {
            var randomBlock = Guid.NewGuid().ToString("N")[..7].ToUpperInvariant();
            var candidate = $"{randomBlock}{suffix}";
            var exists = await dbContext.InventorySales
                .AsNoTracking()
                .AnyAsync(s => !s.IsDeleted && s.ReferenceNumber == candidate);
            if (!exists)
            {
                return candidate;
            }
        }

        // Very unlikely fallback if random collisions happen repeatedly.
        var tail = DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture);
        return $"{tail[^7..]}{suffix}";
    }

    private async Task<string?> SaveEvidenceFileAsync(IFormFile file, int saleId)
    {
        if (file.Length <= 0)
        {
            return null;
        }

        var webRoot = Path.Combine(webHostEnvironment.ContentRootPath, "wwwroot");
        var dir = Path.Combine(webRoot, "uploads", "inventory-evidence");
        Directory.CreateDirectory(dir);
        var ext = Path.GetExtension(file.FileName);
        if (ext.Length > 10)
        {
            ext = string.Empty;
        }

        var fileName = $"{saleId}-{Guid.NewGuid():N}{ext}";
        var fullPath = Path.Combine(dir, fileName);
        await using (var fs = System.IO.File.Create(fullPath))
        {
            await file.CopyToAsync(fs);
        }

        return Path.Combine("uploads", "inventory-evidence", fileName).Replace('\\', '/');
    }

    private static string ResolveEvidenceContentType(string? fileName)
    {
        var ext = Path.GetExtension(fileName ?? "").ToLowerInvariant();
        return ext switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => "application/octet-stream",
        };
    }

    [HttpGet]
    public async Task<IActionResult> GetPaged(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? q = null,
        [FromQuery] int? filterBusinessId = null,
        [FromQuery] int? filterStationId = null)
    {
        if (IsSuperAdmin(User))
        {
            var superPaged = await repository.GetPagedAsync(page, pageSize, q, filterBusinessId, filterStationId);
            await AttachInventoryUserNamesAsync(superPaged.Items);
            return Ok(superPaged);
        }

        if (!TryGetJwtBusiness(out var bid))
        {
            return BadRequest("No business assigned to this user.");
        }

        if (filterBusinessId.HasValue && filterBusinessId.Value != bid)
        {
            return Forbid();
        }

        var stationFilter = ListStationFilter.ForNonSuperAdmin(User, filterStationId);

        var paged = await repository.GetPagedAsync(page, pageSize, q, bid, stationFilter);
        await AttachInventoryUserNamesAsync(paged.Items);
        return Ok(paged);
    }

    [HttpGet("latest-by-nozzle/{nozzleId:int}")]
    public async Task<IActionResult> GetLatestByNozzle(int nozzleId)
    {
        var nozzle = await nozzleRepository.GetByIdAsync(nozzleId);
        if (nozzle is null)
        {
            return NotFound();
        }

        if (!IsSuperAdmin(User))
        {
            if (!TryGetJwtBusiness(out var bid) || nozzle.BusinessId != bid)
            {
                return NotFound();
            }
        }

        var inv = await repository.GetLatestByNozzleIdAsync(nozzleId);
        if (inv is null)
        {
            return Ok(new LatestInventoryForPumpDto
            {
                OpeningLiters = null,
                ClosingLiters = null,
                UsageLiters = null,
            });
        }

        if (!IsSuperAdmin(User) && inv.BusinessId != nozzle.BusinessId)
        {
            return NotFound();
        }

        return Ok(new LatestInventoryForPumpDto
        {
            OpeningLiters = inv.OpeningLiters,
            ClosingLiters = inv.ClosingLiters,
            UsageLiters = inv.UsageLiters,
        });
    }

    /// <summary>Legacy: uses the first nozzle on the pump (same station).</summary>
    [HttpGet("latest-by-pump/{pumpId:int}")]
    public async Task<IActionResult> GetLatestByPump(int pumpId)
    {
        var pump = await pumpRepository.GetByIdAsync(pumpId);
        if (pump is null)
        {
            return NotFound();
        }

        if (!IsSuperAdmin(User))
        {
            if (!TryGetJwtBusiness(out var bid) || pump.BusinessId != bid)
            {
                return NotFound();
            }
        }

        var nozzles = await nozzleRepository.ListByPumpIdAsync(pumpId);
        var first = nozzles.FirstOrDefault();
        if (first is null)
        {
            return Ok(new LatestInventoryForPumpDto
            {
                OpeningLiters = null,
                ClosingLiters = null,
                UsageLiters = null,
            });
        }

        return await GetLatestByNozzle(first.Id);
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

        await AttachInventoryUserNamesAsync(new[] { entity });
        return Ok(entity);
    }

    [HttpGet("sales/{saleId:int}/evidence")]
    public async Task<IActionResult> GetSaleEvidence(int saleId)
    {
        var sale = await dbContext.InventorySales.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == saleId && !x.IsDeleted);
        if (sale is null || string.IsNullOrWhiteSpace(sale.EvidenceFilePath))
        {
            return NotFound();
        }

        if (!IsSuperAdmin(User))
        {
            if (!TryGetJwtBusiness(out var bid) || sale.BusinessId != bid)
            {
                return Forbid();
            }
        }

        var rel = sale.EvidenceFilePath.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
        var full = Path.Combine(webHostEnvironment.ContentRootPath, "wwwroot", rel);
        if (!System.IO.File.Exists(full))
        {
            return NotFound();
        }

        var contentType = ResolveEvidenceContentType(sale.OriginalFileName);
        return PhysicalFile(full, contentType, sale.OriginalFileName ?? Path.GetFileName(full));
    }

    [HttpGet("sales/{saleId:int}")]
    public async Task<IActionResult> GetSaleDetail(int saleId)
    {
        var sale = await dbContext.InventorySales.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == saleId && !x.IsDeleted);
        if (sale is null)
        {
            return NotFound();
        }

        if (!IsSuperAdmin(User))
        {
            if (!TryGetJwtBusiness(out var bid) || sale.BusinessId != bid)
            {
                return Forbid();
            }
        }

        var items = await (
            from item in dbContext.InventoryItems.AsNoTracking()
            where !item.IsDeleted && item.InventorySaleId == saleId
            select new InventoryResponseDto
            {
                Id = item.Id,
                InventorySaleId = sale.Id,
                ReferenceNumber = sale.ReferenceNumber,
                EvidenceFilePath = sale.EvidenceFilePath,
                NozzleId = item.NozzleId,
                OpeningLiters = item.OpeningLiters,
                ClosingLiters = item.ClosingLiters,
                UsageLiters = item.UsageLiters,
                SspLiters = item.SspLiters,
                UsdLiters = item.UsdLiters,
                SspAmount = item.SspAmount,
                UsdAmount = item.UsdAmount,
                SspFuelPrice = item.SspFuelPrice,
                UsdFuelPrice = item.UsdFuelPrice,
                ExchangeRate = item.ExchangeRate,
                UserId = item.UserId,
                Date = item.Date,
                BusinessId = sale.BusinessId,
                StationId = sale.StationId,
            }).OrderByDescending(x => x.Date).ThenByDescending(x => x.Id).ToListAsync();

        await AttachInventoryUserNamesAsync(items);
        var userName = items.FirstOrDefault()?.UserName;
        return Ok(new InventorySaleDetailDto
        {
            SaleId = sale.Id,
            ReferenceNumber = sale.ReferenceNumber,
            BusinessId = sale.BusinessId,
            StationId = sale.StationId,
            UserId = sale.UserId,
            UserName = userName,
            RecordedDate = sale.RecordedDate,
            EvidenceFilePath = sale.EvidenceFilePath,
            OriginalFileName = sale.OriginalFileName,
            Items = items,
        });
    }

    [HttpPut("sales/{saleId:int}/evidence")]
    [RequestSizeLimit(25_000_000)]
    public async Task<IActionResult> UpdateSaleEvidence(int saleId, CancellationToken cancellationToken)
    {
        var sale = await dbContext.InventorySales.FirstOrDefaultAsync(x => x.Id == saleId && !x.IsDeleted, cancellationToken);
        if (sale is null)
        {
            return NotFound();
        }

        if (!IsSuperAdmin(User))
        {
            if (!TryGetJwtBusiness(out var bid) || sale.BusinessId != bid)
            {
                return Forbid();
            }
        }

        if (!Request.HasFormContentType)
        {
            return BadRequest("Use multipart/form-data with file field evidence.");
        }

        var form = await Request.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("evidence") ?? form.Files.FirstOrDefault();
        if (file is null || file.Length == 0)
        {
            return BadRequest("Evidence file is required.");
        }

        var oldRel = sale.EvidenceFilePath;
        var newRel = await SaveEvidenceFileAsync(file, sale.Id);
        if (string.IsNullOrWhiteSpace(newRel))
        {
            return BadRequest("Evidence file was empty.");
        }

        sale.EvidenceFilePath = newRel;
        sale.OriginalFileName = file.FileName;
        sale.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(oldRel))
        {
            var oldFull = Path.Combine(
                webHostEnvironment.ContentRootPath,
                "wwwroot",
                oldRel.Replace('/', Path.DirectorySeparatorChar));
            if (System.IO.File.Exists(oldFull))
            {
                try { System.IO.File.Delete(oldFull); } catch { /* ignore */ }
            }
        }

        return Ok(new { saleId = sale.Id, evidenceFilePath = sale.EvidenceFilePath, originalFileName = sale.OriginalFileName });
    }

    [HttpDelete("sales/{saleId:int}")]
    public async Task<IActionResult> DeleteSale(int saleId, CancellationToken cancellationToken)
    {
        var sale = await dbContext.InventorySales.FirstOrDefaultAsync(x => x.Id == saleId && !x.IsDeleted, cancellationToken);
        if (sale is null)
        {
            return NotFound();
        }

        if (!IsSuperAdmin(User))
        {
            if (!TryGetJwtBusiness(out var bid) || sale.BusinessId != bid)
            {
                return Forbid();
            }
        }

        var items = await dbContext.InventoryItems
            .Where(x => x.InventorySaleId == saleId && !x.IsDeleted)
            .ToListAsync(cancellationToken);

        await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var item in items)
            {
                var restoreErr = await RestoreDippingFromInventoryUsageAsync(item.NozzleId, item.UsageLiters);
                if (restoreErr is not null)
                {
                    await tx.RollbackAsync(cancellationToken);
                    return restoreErr;
                }

                item.IsDeleted = true;
                item.UpdatedAt = DateTime.UtcNow;
            }

            sale.IsDeleted = true;
            sale.UpdatedAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }

        return NoContent();
    }

    private bool ResolveTargetBusinessForBatch(int payloadBusinessId, out int targetBusinessId, out IActionResult? error)
    {
        targetBusinessId = 0;
        error = null;

        if (IsSuperAdmin(User))
        {
            if (payloadBusinessId <= 0)
            {
                error = BadRequest("Select a business for this inventory.");
                return false;
            }

            targetBusinessId = payloadBusinessId;
            return true;
        }

        if (!TryGetJwtBusiness(out var bid))
        {
            error = BadRequest("No business assigned to this user.");
            return false;
        }

        if (payloadBusinessId > 0 && payloadBusinessId != bid)
        {
            error = Forbid();
            return false;
        }

        targetBusinessId = bid;
        return true;
    }

    /// <summary>Save one batch: multiple nozzle lines, one evidence file, one reference number (header per business+station).</summary>
    [HttpPost("batch")]
    [RequestSizeLimit(25_000_000)]
    public async Task<IActionResult> CreateBatch(CancellationToken cancellationToken)
    {
        if (!Request.HasFormContentType)
        {
            return BadRequest("Use multipart/form-data with field payload (JSON).");
        }

        var form = await Request.ReadFormAsync(cancellationToken);
        var payloadStr = form["payload"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(payloadStr))
        {
            return BadRequest("Missing payload (JSON string).");
        }

        var file = form.Files.GetFile("evidence") ?? form.Files.FirstOrDefault();

        InventoryBatchCreatePayloadViewModel? payload;
        try
        {
            payload = JsonSerializer.Deserialize<InventoryBatchCreatePayloadViewModel>(
                payloadStr,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return BadRequest("Invalid payload JSON.");
        }

        if (payload is null || payload.Lines.Count == 0)
        {
            return BadRequest("Add at least one nozzle line.");
        }

        if (!ResolveTargetBusinessForBatch(payload.BusinessId, out var targetBusinessId, out var bizErr))
        {
            return bizErr!;
        }

        if (payload.Lines.GroupBy(l => l.NozzleId).Any(g => g.Count() > 1))
        {
            return BadRequest("Each nozzle can only appear once in a batch.");
        }

        if (payload.StationId <= 0)
        {
            return BadRequest("Station is required.");
        }

        if (!TryGetUserId(out var userId, out var uerr))
        {
            return uerr!;
        }

        var recorded = payload.RecordedAt?.UtcDateTime ?? DateTime.UtcNow;
        var refNum = await GenerateReferenceNumberAsync(targetBusinessId, payload.StationId, recorded);

        var validated = new List<(
            int NozzleId,
            double OpenL,
            double CloseL,
            double UsageL,
            double SspLiters,
            double UsdLiters,
            double SspAmount,
            double UsdAmount,
            double SspFuelPrice,
            double UsdFuelPrice,
            double ExchangeRate)>();

        foreach (var line in payload.Lines)
        {
            if (!double.TryParse(line.OpeningLiters.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var openL))
            {
                return BadRequest($"Invalid opening liters (nozzle {line.NozzleId}).");
            }

            if (!double.TryParse(line.ClosingLiters.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var closeL))
            {
                return BadRequest($"Invalid closing liters (nozzle {line.NozzleId}).");
            }

            if (!double.TryParse(line.SspLiters.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var sspLiters))
            {
                return BadRequest($"Invalid SSP liters (nozzle {line.NozzleId}).");
            }

            if (!double.TryParse(line.UsdLiters.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var usdLiters))
            {
                return BadRequest($"Invalid USD liters (nozzle {line.NozzleId}).");
            }

            var bad = await ValidateStationAndNozzleAsync(targetBusinessId, payload.StationId, line.NozzleId);
            if (bad is not null)
            {
                return bad;
            }

            var usageL = Math.Abs(openL - closeL);
            if (!LiterSplitsEqualUsage(usageL, sspLiters, usdLiters))
            {
                return BadRequest(
                    $"SSP liters + USD liters must equal usage for nozzle {line.NozzleId}.");
            }

            var (fuelTypeId, ftErr) = await ResolveFuelTypeIdForNozzleAsync(line.NozzleId);
            if (ftErr is not null)
            {
                return ftErr;
            }

            var (sspAmount, usdAmount, sspFuelPrice, usdFuelPrice, exchangeRate, moneyErr) = await ComputeInventoryAmountsAsync(
                targetBusinessId,
                payload.StationId,
                fuelTypeId,
                sspLiters,
                usdLiters);
            if (moneyErr is not null)
            {
                return moneyErr;
            }

            validated.Add((line.NozzleId, openL, closeL, usageL, sspLiters, usdLiters, sspAmount, usdAmount, sspFuelPrice, usdFuelPrice, exchangeRate));
        }

        var sale = new InventorySale
        {
            BusinessId = targetBusinessId,
            StationId = payload.StationId,
            UserId = userId,
            RecordedDate = recorded,
            ReferenceNumber = refNum,
            EvidenceFilePath = "",
            OriginalFileName = file?.FileName,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsDeleted = false,
        };

        string? savedRelative = null;
        await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            dbContext.InventorySales.Add(sale);
            await dbContext.SaveChangesAsync(cancellationToken);

            if (file is not null && file.Length > 0)
            {
                savedRelative = await SaveEvidenceFileAsync(file, sale.Id);
                if (string.IsNullOrEmpty(savedRelative))
                {
                    await tx.RollbackAsync(cancellationToken);
                    sale.IsDeleted = true;
                    await dbContext.SaveChangesAsync(cancellationToken);
                    return BadRequest("Evidence file was empty.");
                }

                sale.EvidenceFilePath = savedRelative;
                sale.OriginalFileName = file.FileName;
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            var now = DateTime.UtcNow;
            foreach (var v in validated)
            {
                var dipErr = await SubtractInventoryUsageFromDippingAsync(v.NozzleId, v.UsageL);
                if (dipErr is not null)
                {
                    await tx.RollbackAsync(cancellationToken);
                    if (!string.IsNullOrWhiteSpace(savedRelative))
                    {
                        var fullDel = Path.Combine(
                            webHostEnvironment.ContentRootPath,
                            "wwwroot",
                            savedRelative.Replace('/', Path.DirectorySeparatorChar));
                        if (System.IO.File.Exists(fullDel))
                        {
                            System.IO.File.Delete(fullDel);
                        }
                    }

                    return dipErr;
                }

                dbContext.InventoryItems.Add(new InventoryItem
                {
                    InventorySaleId = sale.Id,
                    NozzleId = v.NozzleId,
                    OpeningLiters = v.OpenL,
                    ClosingLiters = v.CloseL,
                    UsageLiters = v.UsageL,
                    SspLiters = v.SspLiters,
                    UsdLiters = v.UsdLiters,
                    SspAmount = v.SspAmount,
                    UsdAmount = v.UsdAmount,
                    SspFuelPrice = v.SspFuelPrice,
                    UsdFuelPrice = v.UsdFuelPrice,
                    ExchangeRate = v.ExchangeRate,
                    UserId = userId,
                    Date = recorded,
                    CreatedAt = now,
                    UpdatedAt = now,
                    IsDeleted = false,
                });
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            if (savedRelative != null)
            {
                var fullDel = Path.Combine(
                    webHostEnvironment.ContentRootPath,
                    "wwwroot",
                    savedRelative.Replace('/', Path.DirectorySeparatorChar));
                if (System.IO.File.Exists(fullDel))
                {
                    System.IO.File.Delete(fullDel);
                }
            }

            throw;
        }

        var rows = await (
            from item in dbContext.InventoryItems.AsNoTracking()
            join s in dbContext.InventorySales.AsNoTracking() on item.InventorySaleId equals s.Id
            where item.InventorySaleId == sale.Id && !item.IsDeleted && !s.IsDeleted
            select new InventoryResponseDto
            {
                Id = item.Id,
                InventorySaleId = s.Id,
                ReferenceNumber = s.ReferenceNumber,
                EvidenceFilePath = s.EvidenceFilePath,
                NozzleId = item.NozzleId,
                OpeningLiters = item.OpeningLiters,
                ClosingLiters = item.ClosingLiters,
                UsageLiters = item.UsageLiters,
                SspLiters = item.SspLiters,
                UsdLiters = item.UsdLiters,
                SspAmount = item.SspAmount,
                UsdAmount = item.UsdAmount,
                SspFuelPrice = item.SspFuelPrice,
                UsdFuelPrice = item.UsdFuelPrice,
                ExchangeRate = item.ExchangeRate,
                UserId = item.UserId,
                Date = item.Date,
                BusinessId = s.BusinessId,
                StationId = s.StationId,
            }).ToListAsync(cancellationToken);

        await AttachInventoryUserNamesAsync(rows);
        return Ok(new { referenceNumber = sale.ReferenceNumber, saleId = sale.Id, items = rows });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] InventoryWriteRequestViewModel dto)
    {
        if (!ResolveInventoryBusiness(dto, out var targetBusinessId, out var bizErr))
        {
            return bizErr!;
        }

        var existingDto = await repository.GetByIdAsync(id);
        if (existingDto is null)
        {
            return NotFound();
        }

        if (!IsSuperAdmin(User))
        {
            if (!TryGetJwtBusiness(out var bid) || existingDto.BusinessId != bid)
            {
                return Forbid();
            }
        }
        else if (existingDto.BusinessId != targetBusinessId)
        {
            return BadRequest("Inventory belongs to a different business.");
        }

        if (!double.TryParse(NormalizeInventoryLiterString(dto.OpeningLiters), NumberStyles.Any, CultureInfo.InvariantCulture, out var openL))
        {
            return BadRequest("Invalid opening liters.");
        }

        if (!double.TryParse(NormalizeInventoryLiterString(dto.ClosingLiters), NumberStyles.Any, CultureInfo.InvariantCulture, out var closeL))
        {
            return BadRequest("Invalid closing liters.");
        }

        if (!double.TryParse(NormalizeInventoryLiterString(dto.SspLiters), NumberStyles.Any, CultureInfo.InvariantCulture, out var sspLiters))
        {
            return BadRequest("Invalid SSP liters.");
        }

        if (!double.TryParse(NormalizeInventoryLiterString(dto.UsdLiters), NumberStyles.Any, CultureInfo.InvariantCulture, out var usdLiters))
        {
            return BadRequest("Invalid USD liters.");
        }

        var bad = await ValidateStationAndNozzleAsync(targetBusinessId, dto.StationId, dto.NozzleId);
        if (bad is not null)
        {
            return bad;
        }

        var usageL = Math.Abs(openL - closeL);
        if (!LiterSplitsEqualUsage(usageL, sspLiters, usdLiters))
        {
            return BadRequest("SSP liters + USD liters must equal usage liters (opening minus closing, absolute value).");
        }

        if (!TryGetUserId(out var editorUserId, out var uerr))
        {
            return uerr!;
        }

        var (fuelTypeId, ftErr) = await ResolveFuelTypeIdForNozzleAsync(dto.NozzleId);
        if (ftErr is not null)
        {
            return ftErr;
        }

        var (sspAmount, usdAmount, sspFuelPrice, usdFuelPrice, exchangeRate, moneyErr) = await ComputeInventoryAmountsAsync(
            targetBusinessId,
            dto.StationId,
            fuelTypeId,
            sspLiters,
            usdLiters);
        if (moneyErr is not null)
        {
            return moneyErr;
        }

        var rowDate = dto.RecordedAt?.UtcDateTime ?? existingDto.Date;

        var updatedEntity = new InventoryItem
        {
            InventorySaleId = existingDto.InventorySaleId,
            NozzleId = dto.NozzleId,
            OpeningLiters = openL,
            ClosingLiters = closeL,
            UsageLiters = usageL,
            SspLiters = sspLiters,
            UsdLiters = usdLiters,
            SspAmount = sspAmount,
            UsdAmount = usdAmount,
            SspFuelPrice = sspFuelPrice,
            UsdFuelPrice = usdFuelPrice,
            ExchangeRate = exchangeRate,
            UserId = editorUserId,
            Date = rowDate,
            IsDeleted = false,
        };

        await using var tx = await dbContext.Database.BeginTransactionAsync();
        try
        {
            var revErr = await RestoreDippingFromInventoryUsageAsync(existingDto.NozzleId, existingDto.UsageLiters);
            if (revErr is not null)
            {
                await tx.RollbackAsync();
                return revErr;
            }

            var subErr = await SubtractInventoryUsageFromDippingAsync(dto.NozzleId, usageL);
            if (subErr is not null)
            {
                await tx.RollbackAsync();
                return subErr;
            }

            await repository.UpdateItemAsync(id, updatedEntity);
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }

        var updatedDto = await repository.GetByIdAsync(id);
        if (updatedDto is null)
        {
            return NotFound();
        }

        await AttachInventoryUserNamesAsync(new[] { updatedDto });
        return Ok(updatedDto);
    }

    /// <summary>Soft-deletes inventory line only; dipping tank liters are left unchanged.</summary>
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
            if (!TryGetJwtBusiness(out var businessId))
            {
                return BadRequest("No business assigned to this user.");
            }

            if (existing.BusinessId != businessId)
            {
                return Forbid();
            }
        }

        await AttachInventoryUserNamesAsync(new[] { existing });
        await repository.DeleteItemAsync(id);
        return Ok(existing);
    }
}
