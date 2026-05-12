using System.Globalization;
using System.Security.Claims;
using gas_station.Common;
using gas_station.Data.Context;
using gas_station.Data.Interfaces;
using gas_station.Data.Repository;
using gas_station.Models;
using gas_station.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace gas_station.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CustomerFuelGivensController(
    ICustomerFuelGivenRepository repository,
    IStationRepository stationRepository,
    IDippingRepository dippingRepository,
    ICustomerPaymentRepository customerPaymentRepository,
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

    private bool TryGetUserId(out int userId)
    {
        userId = 0;
        var uid = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return !string.IsNullOrEmpty(uid) && int.TryParse(uid, out userId);
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

    private async Task<(int currencyId, string? error)> ResolveCustomerFuelCurrencyIdAsync(int requestedCurrencyId)
    {
        if (requestedCurrencyId > 0)
        {
            var ok = await dbContext.Currencies.AnyAsync(c => !c.IsDeleted && c.Id == requestedCurrencyId);
            if (!ok) return (0, "Invalid currency.");
            return (requestedCurrencyId, null);
        }

        var sspId = await dbContext.Currencies.AsNoTracking()
            .Where(c => !c.IsDeleted && c.Code == "SSP")
            .Select(c => (int?)c.Id)
            .FirstOrDefaultAsync();
        if (sspId.HasValue) return (sspId.Value, null);

        var anyId = await dbContext.Currencies.AsNoTracking()
            .Where(c => !c.IsDeleted)
            .OrderBy(c => c.Id)
            .Select(c => (int?)c.Id)
            .FirstOrDefaultAsync();
        if (anyId.HasValue) return (anyId.Value, null);
        return (0, "No currencies configured.");
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

    private async Task<Customer> GetOrCreateCustomerAsync(
        int businessId,
        int stationId,
        string name,
        string phone)
    {
        var n = name.Trim();
        var p = phone.Trim();
        var existing = await dbContext.Customers
            .FirstOrDefaultAsync(x => !x.IsDeleted
                                      && x.BusinessId == businessId
                                      && x.Name == n
                                      && x.Phone == p);
        if (existing is not null) return existing;

        var now = DateTime.UtcNow;
        var customer = new Customer
        {
            Name = n,
            Phone = p,
            StationId = stationId,
            BusinessId = businessId,
            CreatedAt = now,
            UpdatedAt = now,
            IsDeleted = false,
        };
        await dbContext.Customers.AddAsync(customer);
        await dbContext.SaveChangesAsync();
        return customer;
    }

    /// <summary>
    /// <paramref name="id"/> is normally <see cref="Customer"/> PK. If no row matches, treats <paramref name="id"/>
    /// as a legacy <see cref="CustomerFuelTransaction"/> id and returns that transaction&apos;s customer.
    /// </summary>
    private async Task<Customer?> ResolveCustomerFromCustomerOrTransactionIdAsync(int id)
    {
        var byPk = await dbContext.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => !c.IsDeleted && c.Id == id);
        if (byPk is not null)
            return byPk;

        var seed = await repository.GetByIdAsync(id);
        if (seed is null)
            return null;

        return await dbContext.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => !c.IsDeleted && c.Id == seed.CustomerId);
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

    /// <summary>
    /// One row per customer with a positive ledger balance (sum charged − sum paid within the business).
    /// Station filter restricts to customers who have at least one fuel/cash transaction at that station.
    /// </summary>
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

        HashSet<int>? allowedCustomerIds = null;
        if (stationFilter is > 0)
        {
            allowedCustomerIds = (await dbContext.CustomerFuelGivens.AsNoTracking()
                    .Where(x => !x.IsDeleted && x.BusinessId == bid && x.StationId == stationFilter.Value)
                    .Select(x => x.CustomerId)
                    .Distinct()
                    .ToListAsync())
                .ToHashSet();
            if (allowedCustomerIds.Count == 0)
                return Ok(Array.Empty<object>());
        }

        var ledgerRows = await dbContext.CustomerPayments.AsNoTracking()
            .Where(p => !p.IsDeleted && p.BusinessId == bid)
            .GroupBy(p => p.CustomerId)
            .Select(g => new
            {
                CustomerId = g.Key,
                TotalCharged = g.Sum(x => x.ChargedAmount),
                TotalPaid = g.Sum(x => x.AmountPaid),
            })
            .ToListAsync();

        var owing = ledgerRows
            .Select(x => (CustomerId: x.CustomerId, TotalCharged: x.TotalCharged, TotalPaid: x.TotalPaid))
            .Select(x => (x.CustomerId, x.TotalCharged, x.TotalPaid, Balance: x.TotalCharged - x.TotalPaid))
            .Where(x => x.Balance > 0.0001)
            .ToList();

        if (allowedCustomerIds is not null)
            owing = owing.Where(x => allowedCustomerIds.Contains(x.CustomerId)).ToList();

        if (owing.Count == 0)
            return Ok(Array.Empty<object>());

        var owingIds = owing.Select(x => x.CustomerId).ToList();
        var customerMap = await dbContext.Customers.AsNoTracking()
            .Where(c => !c.IsDeleted && owingIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name, c.Phone })
            .ToDictionaryAsync(c => c.Id);

        var latestFuelDates = await dbContext.CustomerFuelGivens.AsNoTracking()
            .Where(t => !t.IsDeleted && t.BusinessId == bid && owingIds.Contains(t.CustomerId))
            .Select(t => new { t.CustomerId, t.Date, t.Id, t.StationId })
            .ToListAsync();

        var latestFuelByCustomer = latestFuelDates
            .GroupBy(t => t.CustomerId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.Date).ThenByDescending(x => x.Id).First());

        var lastPaymentDates = await dbContext.CustomerPayments.AsNoTracking()
            .Where(p => !p.IsDeleted && p.BusinessId == bid && owingIds.Contains(p.CustomerId))
            .GroupBy(p => p.CustomerId)
            .Select(g => new { CustomerId = g.Key, LastDate = g.Max(x => x.PaymentDate) })
            .ToDictionaryAsync(x => x.CustomerId, x => x.LastDate);

        var rows = new List<object>();
        foreach (var o in owing.OrderByDescending(x => x.Balance))
        {
            if (!customerMap.TryGetValue(o.CustomerId, out var cust))
                continue;

            var candidates = new List<DateTime>(2);
            var stationId = 0;
            if (latestFuelByCustomer.TryGetValue(o.CustomerId, out var lastFuel) && lastFuel != null)
            {
                candidates.Add(lastFuel.Date);
                stationId = lastFuel.StationId;
            }

            if (lastPaymentDates.TryGetValue(o.CustomerId, out var lastPayUtc))
                candidates.Add(lastPayUtc);

            var lastActivityUtc = candidates.Count > 0 ? candidates.Max() : DateTime.UtcNow;

            rows.Add(new
            {
                customerId = o.CustomerId,
                id = o.CustomerId,
                name = cust.Name,
                phone = cust.Phone,
                totalDue = Math.Round(o.TotalCharged, 2, MidpointRounding.AwayFromZero),
                totalPaid = Math.Round(o.TotalPaid, 2, MidpointRounding.AwayFromZero),
                balance = Math.Round(o.Balance, 2, MidpointRounding.AwayFromZero),
                date = lastActivityUtc,
                stationId,
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

    [HttpGet("customers")]
    public async Task<IActionResult> GetCustomers(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? q = null,
        [FromQuery] int? filterStationId = null)
    {
        int? businessFilter = null;
        if (!IsSuperAdmin(User))
        {
            if (!TryGetJwtBusiness(out var bid))
                return BadRequest("No business assigned to this user.");
            businessFilter = bid;
        }

        var stationFilter = IsSuperAdmin(User)
            ? (filterStationId is > 0 ? filterStationId : null)
            : ListStationFilter.ForNonSuperAdmin(User, filterStationId);

        var customersQuery = dbContext.Customers.AsNoTracking().Where(c => !c.IsDeleted);
        if (businessFilter is > 0)
            customersQuery = customersQuery.Where(c => c.BusinessId == businessFilter.Value);
        if (stationFilter is > 0)
            customersQuery = customersQuery.Where(c => c.StationId == stationFilter.Value);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            customersQuery = customersQuery.Where(c =>
                EF.Functions.Like(c.Name, $"%{s}%") ||
                EF.Functions.Like(c.Phone, $"%{s}%"));
        }

        // Hide migration placeholder / junk customers (never listed in UI).
        // Use ToLower() — EF Core cannot translate String.Equals(..., StringComparison) on MySQL.
        customersQuery = customersQuery.Where(c =>
            !string.IsNullOrWhiteSpace(c.Name) &&
            !(string.IsNullOrWhiteSpace(c.Phone)
              && c.Name != null
              && c.Name.Trim().ToLower() == "unknown"));

        var custRows = await customersQuery
            .Select(c => new { c.Id, c.Name, c.Phone, c.BusinessId, c.StationId, c.CreatedAt })
            .ToListAsync();

        if (custRows.Count == 0)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 500);
            return Ok(new { items = Array.Empty<CustomerFuelGivenCustomerRowDto>(), totalCount = 0, page, pageSize });
        }

        var custIds = custRows.Select(c => c.Id).ToList();

        var payQ = dbContext.CustomerPayments.AsNoTracking()
            .Where(p => !p.IsDeleted && custIds.Contains(p.CustomerId));
        if (businessFilter is > 0)
            payQ = payQ.Where(p => p.BusinessId == businessFilter.Value);

        var payAgg = await payQ
            .GroupBy(p => p.CustomerId)
            .Select(g => new
            {
                CustomerId = g.Key,
                Charged = g.Sum(x => x.ChargedAmount),
                Paid = g.Sum(x => x.AmountPaid),
                LastPay = g.Max(x => x.PaymentDate),
            })
            .ToDictionaryAsync(x => x.CustomerId, x => x);

        var fuelQ = dbContext.CustomerFuelGivens.AsNoTracking()
            .Where(x => !x.IsDeleted && custIds.Contains(x.CustomerId));
        if (businessFilter is > 0)
            fuelQ = fuelQ.Where(x => x.BusinessId == businessFilter.Value);
        if (stationFilter is > 0)
            fuelQ = fuelQ.Where(x => x.StationId == stationFilter.Value);

        var lastFuelByCustomer = await fuelQ
            .GroupBy(x => x.CustomerId)
            .Select(g => new
            {
                CustomerId = g.Key,
                LastDate = g.Max(x => x.Date),
                LastStationId = g.OrderByDescending(x => x.Date).ThenByDescending(x => x.Id).Select(x => x.StationId).First(),
            })
            .ToDictionaryAsync(x => x.CustomerId, x => x);

        var rows = custRows
            .Select(c =>
            {
                payAgg.TryGetValue(c.Id, out var p);
                var charged = p?.Charged ?? 0;
                var paid = p?.Paid ?? 0;
                lastFuelByCustomer.TryGetValue(c.Id, out var lf);
                var candidates = new List<DateTime> { c.CreatedAt };
                if (lf is not null)
                    candidates.Add(lf.LastDate);
                if (p is not null)
                    candidates.Add(p.LastPay);
                var lastDate = candidates.Max();
                var stationId = lf?.LastStationId ?? c.StationId;
                return new CustomerFuelGivenCustomerRowDto
                {
                    Id = c.Id,
                    Name = c.Name,
                    Phone = c.Phone,
                    BusinessId = c.BusinessId,
                    StationId = stationId,
                    LastDate = lastDate,
                    TotalDue = Math.Round(charged, 2, MidpointRounding.AwayFromZero),
                    TotalPaid = Math.Round(paid, 2, MidpointRounding.AwayFromZero),
                    Balance = Math.Round(charged - paid, 2, MidpointRounding.AwayFromZero),
                };
            })
            .OrderByDescending(x => x.LastDate)
            .ThenBy(x => x.Name)
            .ToList();

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 500);
        var total = rows.Count;
        var items = rows.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return Ok(new { items, totalCount = total, page, pageSize });
    }

    [HttpGet("customers/{id:int}")]
    public async Task<IActionResult> GetCustomerById(int id)
    {
        var customer = await ResolveCustomerFromCustomerOrTransactionIdAsync(id);
        if (customer is null)
            return NotFound();

        if (!IsSuperAdmin(User))
        {
            if (!TryGetJwtBusiness(out var bid) || customer.BusinessId != bid)
                return Forbid();
        }

        var rows = await dbContext.CustomerPayments.AsNoTracking()
            .Where(x => !x.IsDeleted
                        && x.BusinessId == customer.BusinessId
                        && x.CustomerId == customer.Id)
            .Select(x => new { x.ChargedAmount, x.AmountPaid })
            .ToListAsync();
        var charged = rows.Sum(x => x.ChargedAmount);
        var paid = rows.Sum(x => x.AmountPaid);

        var lastFuel = await dbContext.CustomerFuelGivens.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BusinessId == customer.BusinessId && x.CustomerId == customer.Id)
            .OrderByDescending(x => x.Date).ThenByDescending(x => x.Id)
            .Select(x => new { x.Date, x.StationId })
            .FirstOrDefaultAsync();

        var payDatesQuery = dbContext.CustomerPayments.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BusinessId == customer.BusinessId && x.CustomerId == customer.Id);
        DateTime? lastPay = await payDatesQuery.AnyAsync()
            ? await payDatesQuery.MaxAsync(x => x.PaymentDate)
            : null;

        var candidates = new List<DateTime> { customer.CreatedAt };
        if (lastFuel is not null)
            candidates.Add(lastFuel.Date);
        if (lastPay.HasValue)
            candidates.Add(lastPay.Value);
        var lastDate = candidates.Max();
        var stationId = lastFuel?.StationId ?? customer.StationId;

        return Ok(new CustomerFuelGivenCustomerRowDto
        {
            Id = customer.Id,
            Name = customer.Name,
            Phone = customer.Phone,
            BusinessId = customer.BusinessId,
            StationId = stationId,
            LastDate = lastDate,
            TotalDue = Math.Round(charged, 2, MidpointRounding.AwayFromZero),
            TotalPaid = Math.Round(paid, 2, MidpointRounding.AwayFromZero),
            Balance = Math.Round(charged - paid, 2, MidpointRounding.AwayFromZero),
        });
    }

    [HttpPost("customers")]
    public async Task<IActionResult> CreateCustomer([FromBody] CustomerWriteRequestViewModel dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest("Name is required.");
        if (string.Equals(dto.Name.Trim(), "Unknown", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Customer name cannot be \"Unknown\".");

        int targetBusinessId;
        if (IsSuperAdmin(User))
        {
            if (dto.BusinessId <= 0) return BadRequest("Select a business.");
            targetBusinessId = dto.BusinessId;
        }
        else
        {
            if (!TryGetJwtBusiness(out targetBusinessId))
                return BadRequest("No business assigned to this user.");
            if (dto.BusinessId > 0 && dto.BusinessId != targetBusinessId)
                return Forbid();
        }

        var stationId = dto.StationId;
        if (stationId <= 0 && TryGetJwtStation(out var jwtStation) && jwtStation > 0)
            stationId = jwtStation;
        if (stationId <= 0)
            return BadRequest("Station is required.");

        var bad = await ValidateStationAsync(targetBusinessId, stationId);
        if (bad is not null) return bad;

        var customer = await GetOrCreateCustomerAsync(targetBusinessId, stationId, dto.Name, dto.Phone ?? string.Empty);
        var latest = await dbContext.CustomerFuelGivens.AsNoTracking()
            .Where(x => !x.IsDeleted && x.CustomerId == customer.Id)
            .OrderByDescending(x => x.Date).ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync();

        return Ok(new CustomerFuelGivenCustomerRowDto
        {
            Id = customer.Id,
            Name = customer.Name,
            Phone = customer.Phone,
            BusinessId = customer.BusinessId,
            StationId = customer.StationId,
            LastDate = latest?.Date ?? customer.CreatedAt,
            TotalDue = 0,
            TotalPaid = 0,
            Balance = 0,
        });
    }

    [HttpGet("customers/{id:int}/transactions")]
    public async Task<IActionResult> GetCustomerTransactions(int id)
    {
        var customer = await ResolveCustomerFromCustomerOrTransactionIdAsync(id);
        if (customer is null)
            return NotFound();

        if (!IsSuperAdmin(User))
        {
            if (!TryGetJwtBusiness(out var bid) || customer.BusinessId != bid)
                return Forbid();
        }

        var rows = await dbContext.CustomerFuelGivens.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BusinessId == customer.BusinessId && x.CustomerId == customer.Id)
            .OrderByDescending(x => x.Date)
            .ThenByDescending(x => x.Id)
            .ToListAsync();

        return Ok(rows);
    }

    [HttpPut("customers/{id:int}")]
    public async Task<IActionResult> UpdateCustomer(int id, [FromBody] CustomerIdentityWriteRequestViewModel dto)
    {
        var resolved = await ResolveCustomerFromCustomerOrTransactionIdAsync(id);
        if (resolved is null)
            return NotFound();
        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest("Name is required.");

        if (!IsSuperAdmin(User))
        {
            if (!TryGetJwtBusiness(out var bid) || resolved.BusinessId != bid)
                return Forbid();
        }

        var newName = dto.Name.Trim();
        var newPhone = dto.Phone?.Trim() ?? string.Empty;
        await using var tx = await dbContext.Database.BeginTransactionAsync();
        try
        {
            var customer = await dbContext.Customers.FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == resolved.Id);
            if (customer is not null)
            {
                customer.Name = newName;
                customer.Phone = newPhone;
                customer.UpdatedAt = DateTime.UtcNow;
            }

            await dbContext.SaveChangesAsync();
            await customerPaymentRepository.RecalculateCustomerBalancesAsync(resolved.BusinessId, resolved.Id);
            await tx.CommitAsync();
            return Ok(new { id = resolved.Id, name = newName, phone = newPhone });
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    [HttpDelete("customers/{id:int}")]
    public async Task<IActionResult> DeleteCustomer(int id)
    {
        var resolved = await ResolveCustomerFromCustomerOrTransactionIdAsync(id);
        if (resolved is null)
            return NotFound();

        if (!IsSuperAdmin(User))
        {
            if (!TryGetJwtBusiness(out var bid) || resolved.BusinessId != bid)
                return Forbid();
        }

        await using var tx = await dbContext.Database.BeginTransactionAsync();
        try
        {
            var givens = await dbContext.CustomerFuelGivens
                .Where(x => !x.IsDeleted && x.BusinessId == resolved.BusinessId && x.CustomerId == resolved.Id)
                .ToListAsync();

            foreach (var g in givens)
            {
                var ledger = await dbContext.CustomerPayments
                    .Where(x => !x.IsDeleted && x.CustomerId == g.CustomerId && x.Description == "Charged")
                    .ToListAsync();
                foreach (var r in ledger)
                {
                    r.IsDeleted = true;
                    r.UpdatedAt = DateTime.UtcNow;
                }

                if (string.Equals(g.Type, "Fuel", StringComparison.OrdinalIgnoreCase))
                {
                    var restoreErr = await RestoreToDippingAsync(g.StationId, g.FuelTypeId, g.GivenLiter);
                    if (restoreErr is not null)
                    {
                        await tx.RollbackAsync();
                        return restoreErr;
                    }
                }

                g.IsDeleted = true;
                g.UpdatedAt = DateTime.UtcNow;
            }

            var customer = await dbContext.Customers.FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == resolved.Id);
            if (customer is not null)
            {
                customer.IsDeleted = true;
                customer.UpdatedAt = DateTime.UtcNow;
            }

            var namePhonePayments = await dbContext.CustomerPayments
                .Where(x => !x.IsDeleted && x.BusinessId == resolved.BusinessId && x.CustomerId == resolved.Id)
                .ToListAsync();
            foreach (var p in namePhonePayments)
            {
                p.IsDeleted = true;
                p.UpdatedAt = DateTime.UtcNow;
            }

            await dbContext.SaveChangesAsync();
            await tx.CommitAsync();
            return Ok(new { deletedTransactions = givens.Count, deletedPayments = namePhonePayments.Count });
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
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

        if (!TryGetUserId(out var userId))
            return Unauthorized();

        var type = NormalizeType(dto.Type);
        if (!TryParseFields(dto, type, out var givenLiter, out var price, out var amountUsd, out var cashAmount, out var parseErr))
            return BadRequest(parseErr);

        var (currencyId, curErr) = await ResolveCustomerFuelCurrencyIdAsync(dto.CurrencyId);
        if (curErr != null)
            return BadRequest(curErr);

        int resolvedCustomerId;
        if (dto.CustomerId > 0)
        {
            var existingCustomer = await dbContext.Customers.AsNoTracking()
                .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == dto.CustomerId && x.BusinessId == targetBusinessId);
            if (existingCustomer is null)
                return BadRequest("Customer not found in this business.");
            if (string.Equals(existingCustomer.Name.Trim(), "Unknown", StringComparison.OrdinalIgnoreCase))
                return BadRequest("Invalid customer.");
            resolvedCustomerId = existingCustomer.Id;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                return BadRequest("Name is required.");
            if (string.IsNullOrWhiteSpace((dto.Phone ?? string.Empty).Trim()))
                return BadRequest("Phone is required.");
            if (string.Equals(dto.Name.Trim(), "Unknown", StringComparison.OrdinalIgnoreCase))
                return BadRequest("Customer name cannot be \"Unknown\".");
            resolvedCustomerId = 0;
        }

        var entity = new CustomerFuelTransaction
        {
            CustomerId = 0,
            Type = type,
            FuelTypeId = type == "Fuel" ? dto.FuelTypeId : 0,
            GivenLiter = type == "Fuel" ? givenLiter : 0,
            Price = type == "Fuel" ? price : 0,
            UsdAmount = type == "Fuel" ? amountUsd : 0,
            CashAmount = type == "Cash" ? cashAmount : 0,
            Remark = string.IsNullOrWhiteSpace(dto.Remark) ? null : dto.Remark.Trim(),
            StationId = dto.StationId,
            BusinessId = targetBusinessId,
            Date = dto.Date?.UtcDateTime ?? DateTime.UtcNow,
            CurrencyId = currencyId,
        };

        await using var tx = await dbContext.Database.BeginTransactionAsync();
        try
        {
            Customer customer;
            if (resolvedCustomerId > 0)
            {
                entity.CustomerId = resolvedCustomerId;
                var custRow = await dbContext.Customers.FirstOrDefaultAsync(x =>
                    !x.IsDeleted && x.Id == resolvedCustomerId && x.BusinessId == targetBusinessId);
                if (custRow is null)
                {
                    await tx.RollbackAsync();
                    return BadRequest("Customer not found in this business.");
                }
                customer = custRow;
            }
            else
            {
                customer = await GetOrCreateCustomerAsync(
                    targetBusinessId,
                    dto.StationId,
                    dto.Name,
                    dto.Phone ?? string.Empty);
                entity.CustomerId = customer.Id;
            }

            if (type == "Fuel")
            {
                var dipErr = await SubtractFromDippingAsync(entity.StationId, entity.FuelTypeId, entity.GivenLiter);
                if (dipErr is not null)
                {
                    await tx.RollbackAsync();
                    return dipErr;
                }
            }

            var added = await repository.AddAsync(entity);

            // Auto-create the "Charged" ledger row mirroring the supplier flow.
            var charged = CustomerPaymentRepository.ChargedFromCfg(added);
            var refNo = await customerPaymentRepository.GenerateReferenceAsync(targetBusinessId, added.Date);
            await customerPaymentRepository.AddAsync(new CustomerPayment
            {
                CustomerId = customer.Id,
                ReferenceNo = refNo,
                Description = "Charged",
                ChargedAmount = charged,
                AmountPaid = 0,
                Balance = 0,
                PaymentDate = added.Date,
                BusinessId = targetBusinessId,
                UserId = userId
            });
            await customerPaymentRepository.SyncCustomerChargedTotalAndRecalculateBalancesAsync(targetBusinessId, customer.Id, userId);

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

        var type = NormalizeType(dto.Type);
        if (!TryParseFields(dto, type, out var givenLiter, out var price, out var amountUsd, out var cashAmount, out var parseErr))
            return BadRequest(parseErr);

        var (currencyId, curErr) = await ResolveCustomerFuelCurrencyIdAsync(dto.CurrencyId);
        if (curErr != null)
            return BadRequest(curErr);

        var prevWasFuel = string.Equals(existing.Type, "Fuel", StringComparison.OrdinalIgnoreCase);

        if (!TryGetUserId(out var userId))
            return Unauthorized();

        await using var tx = await dbContext.Database.BeginTransactionAsync();
        try
        {
            if (dto.CustomerId > 0 && dto.CustomerId != existing.CustomerId)
            {
                await tx.RollbackAsync();
                return BadRequest("customerId does not match this transaction's customer.");
            }

            if (prevWasFuel)
            {
                var restoreErr = await RestoreToDippingAsync(existing.StationId, existing.FuelTypeId, existing.GivenLiter);
                if (restoreErr is not null)
                {
                    await tx.RollbackAsync();
                    return restoreErr;
                }
            }

            if (type == "Fuel")
            {
                var dipErr = await SubtractFromDippingAsync(dto.StationId, dto.FuelTypeId, givenLiter);
                if (dipErr is not null)
                {
                    await tx.RollbackAsync();
                    return dipErr;
                }
            }

            var linked = await dbContext.Customers.AsNoTracking()
                .FirstOrDefaultAsync(c => !c.IsDeleted && c.Id == existing.CustomerId && c.BusinessId == targetBusinessId);
            if (linked is null)
            {
                await tx.RollbackAsync();
                return BadRequest("Customer record for this transaction was not found.");
            }

            existing.Type = type;
            existing.FuelTypeId = type == "Fuel" ? dto.FuelTypeId : 0;
            existing.GivenLiter = type == "Fuel" ? givenLiter : 0;
            existing.Price = type == "Fuel" ? price : 0;
            existing.UsdAmount = type == "Fuel" ? amountUsd : 0;
            existing.CashAmount = type == "Cash" ? cashAmount : 0;
            existing.Remark = string.IsNullOrWhiteSpace(dto.Remark) ? null : dto.Remark.Trim();
            existing.StationId = dto.StationId;
            existing.Date = dto.Date?.UtcDateTime ?? existing.Date;
            existing.CurrencyId = currencyId;

            var updated = await repository.UpdateAsync(id, existing);
            await customerPaymentRepository.SyncCustomerChargedTotalAndRecalculateBalancesAsync(targetBusinessId, updated.CustomerId, userId);

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

        if (!TryGetUserId(out var userId))
            return Unauthorized();

        await using var tx = await dbContext.Database.BeginTransactionAsync();
        try
        {
            // Soft-delete the linked Charged ledger row(s) before removing the cfg.
            var ledger = await dbContext.CustomerPayments
                .Where(x => !x.IsDeleted && x.CustomerId == existing.CustomerId && x.Description == "Charged")
                .ToListAsync();
            foreach (var r in ledger)
            {
                r.IsDeleted = true;
                r.UpdatedAt = DateTime.UtcNow;
            }
            if (ledger.Count > 0) await dbContext.SaveChangesAsync();

            // Restore liters to dipping for fuel rows.
            if (string.Equals(existing.Type, "Fuel", StringComparison.OrdinalIgnoreCase))
            {
                var restoreErr = await RestoreToDippingAsync(existing.StationId, existing.FuelTypeId, existing.GivenLiter);
                if (restoreErr is not null)
                {
                    await tx.RollbackAsync();
                    return restoreErr;
                }
            }

            var deleted = await repository.DeleteAsync(id);
            await customerPaymentRepository.SyncCustomerChargedTotalAndRecalculateBalancesAsync(existing.BusinessId, existing.CustomerId, userId);
            await tx.CommitAsync();
            return Ok(deleted);
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    private static string NormalizeType(string? value)
    {
        if (string.Equals(value, "Cash", StringComparison.OrdinalIgnoreCase)) return "Cash";
        return "Fuel";
    }

    private static bool TryParseFields(
        CustomerFuelGivenWriteRequestViewModel dto,
        string type,
        out double givenLiter,
        out double price,
        out double amountUsd,
        out double cashAmount,
        out string error)
    {
        givenLiter = 0;
        price = 0;
        amountUsd = 0;
        cashAmount = 0;
        error = string.Empty;

        if (type == "Fuel")
        {
            if (!double.TryParse((dto.GivenLiter ?? "0").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out givenLiter) || givenLiter <= 0)
            {
                error = "Invalid given liter.";
                return false;
            }
            if (!double.TryParse((dto.Price ?? "0").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out price) || price < 0)
            {
                error = "Invalid price.";
                return false;
            }
            if (!double.TryParse((dto.AmountUsd ?? "0").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out amountUsd) || amountUsd < 0)
            {
                error = "Invalid amount USD.";
                return false;
            }
        }
        else
        {
            if (!double.TryParse((dto.CashAmount ?? "0").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out cashAmount) || cashAmount <= 0)
            {
                error = "Invalid cash amount.";
                return false;
            }
        }

        return true;
    }
}

public sealed class CustomerFuelGivenCustomerRowDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public double TotalDue { get; set; }
    public double TotalPaid { get; set; }
    public double Balance { get; set; }
    public DateTime LastDate { get; set; }
    public int StationId { get; set; }
    public int BusinessId { get; set; }
}

public sealed class CustomerIdentityWriteRequestViewModel
{
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
}
