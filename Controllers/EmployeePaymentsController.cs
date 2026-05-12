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
public class EmployeePaymentsController(
    IEmployeePaymentRepository repository,
    IEmployeeRepository employeeRepository,
    GasStationDBContext dbContext) : ControllerBase
{
    private const string SuperAdminRole = "SuperAdmin";
    private static bool IsSuperAdmin(ClaimsPrincipal user) => user.IsInRole(SuperAdminRole);

    private static readonly string[] AllowedDescriptions = ["Payment", "Salary", "Advance", "Bonus"];

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

    private static string NormalizeDescription(string? raw)
    {
        var trimmed = (raw ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed)) return "Payment";
        var match = AllowedDescriptions.FirstOrDefault(a => string.Equals(a, trimmed, StringComparison.OrdinalIgnoreCase));
        return match ?? "Payment";
    }

    [HttpGet]
    public async Task<IActionResult> GetPaged(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? q = null,
        [FromQuery] int? businessId = null,
        [FromQuery] int? filterStationId = null,
        [FromQuery] int? employeeId = null,
        [FromQuery] string? period = null)
    {
        if (IsSuperAdmin(User))
            return Ok(await repository.GetPagedAsync(page, pageSize, q, businessId, filterStationId, employeeId, period));

        if (!TryGetJwtBusiness(out var bid))
            return BadRequest("No business assigned to this user.");

        var stationScope = ListStationFilter.ForNonSuperAdmin(User, filterStationId);
        return Ok(await repository.GetPagedAsync(page, pageSize, q, bid, stationScope, employeeId, period));
    }

    /// <summary>Outstanding balance for an employee within a business.</summary>
    [HttpGet("balance")]
    public async Task<IActionResult> Balance(
        [FromQuery] int employeeId,
        [FromQuery] int? businessId = null)
    {
        if (employeeId <= 0) return BadRequest("employeeId is required.");

        int bid;
        if (IsSuperAdmin(User))
        {
            if (businessId is not > 0) return BadRequest("businessId is required.");
            bid = businessId.Value;
        }
        else
        {
            if (!TryGetJwtBusiness(out bid)) return BadRequest("No business assigned to this user.");
            if (businessId is > 0 && businessId.Value != bid) return Forbid();
        }

        var employee = await employeeRepository.GetByIdAsync(employeeId);
        if (employee is null || employee.BusinessId != bid) return NotFound();

        var balance = await repository.GetEmployeeBalanceAsync(bid, employeeId);
        var rows = await dbContext.EmployeePayments.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BusinessId == bid && x.EmployeeId == employeeId)
            .Select(x => new { x.ChargedAmount, x.PaidAmount })
            .ToListAsync();
        var totalDue = rows.Sum(x => x.ChargedAmount);
        var totalPaid = rows.Sum(x => x.PaidAmount);

        return Ok(new
        {
            employeeId,
            name = employee.Name,
            phone = employee.Phone,
            position = employee.Position,
            baseSalary = employee.BaseSalary,
            totalDue = Math.Round(totalDue, 2, MidpointRounding.AwayFromZero),
            totalPaid = Math.Round(totalPaid, 2, MidpointRounding.AwayFromZero),
            balance = Math.Round(Math.Max(0, balance), 2, MidpointRounding.AwayFromZero),
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] EmployeePaymentWriteRequestViewModel dto)
    {
        if (dto.EmployeeId <= 0) return BadRequest("employeeId is required.");
        if (!ResolveBusiness(dto.BusinessId, out var bid, out var bizErr)) return bizErr!;
        if (!TryGetUserId(out var userId, out var uerr)) return uerr!;

        if (!double.TryParse((dto.AmountPaid ?? "0").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var paid) || paid < 0)
            return BadRequest("Invalid paid amount.");

        var charged = 0d;
        if (!string.IsNullOrWhiteSpace(dto.ChargedAmount) &&
            double.TryParse(dto.ChargedAmount.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var c) &&
            c >= 0)
        {
            charged = c;
        }

        if (paid <= 0 && charged <= 0)
            return BadRequest("Provide a paid amount or a charged amount.");

        var employee = await employeeRepository.GetByIdAsync(dto.EmployeeId);
        if (employee is null || employee.BusinessId != bid)
            return BadRequest("Employee not found in this business.");

        var paymentDate = dto.PaymentDate?.UtcDateTime ?? DateTime.UtcNow;
        var refNo = await repository.GenerateReferenceAsync(bid, paymentDate);
        var description = NormalizeDescription(dto.Description);
        // If only a charged amount was supplied, treat the row as a "Salary" accrual.
        if (charged > 0 && paid <= 0 && string.Equals(description, "Payment", StringComparison.OrdinalIgnoreCase))
            description = "Salary";

        var row = new EmployeePayment
        {
            EmployeeId = employee.Id,
            ReferenceNo = refNo,
            Description = description,
            ChargedAmount = Math.Round(charged, 2, MidpointRounding.AwayFromZero),
            PaidAmount = Math.Round(paid, 2, MidpointRounding.AwayFromZero),
            Balance = 0,
            PaymentDate = paymentDate,
            PeriodLabel = string.IsNullOrWhiteSpace(dto.PeriodLabel) ? null : dto.PeriodLabel.Trim(),
            BusinessId = bid,
            UserId = userId,
            StationId = dto.StationId is > 0 ? dto.StationId : (employee.StationId is > 0 ? employee.StationId : null),
        };

        var added = await repository.AddAsync(row);
        await repository.RecalculateEmployeeBalancesAsync(bid, employee.Id);
        return Ok(added);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var row = await repository.GetByIdAsync(id);
        if (row is null) return NotFound();
        if (!IsSuperAdmin(User) && (!TryGetJwtBusiness(out var bid) || row.BusinessId != bid)) return Forbid();
        var deleted = await repository.DeleteAsync(id);
        await repository.RecalculateEmployeeBalancesAsync(row.BusinessId, row.EmployeeId);
        return Ok(deleted);
    }

    /// <summary>
    /// Records a batch payroll run for a period. Each non-excluded item generates a Salary charge
    /// row + a Payment row. Returns the created ledger rows so the client can update its caches.
    /// </summary>
    [HttpPost("payroll-run")]
    public async Task<IActionResult> PayrollRun([FromBody] PayrollRunWriteRequestViewModel dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Period))
            return BadRequest("Period is required, e.g. \"2026-05\".");

        if (dto.Items is null || dto.Items.Count == 0)
            return BadRequest("Provide at least one payroll item.");

        if (!ResolveBusiness(dto.BusinessId, out var bid, out var bizErr)) return bizErr!;
        if (!TryGetUserId(out var userId, out var uerr)) return uerr!;

        var stationId = dto.StationId is > 0 ? dto.StationId : null;
        var paymentDate = dto.PaymentDate?.UtcDateTime ?? DateTime.UtcNow;
        var period = dto.Period.Trim();

        var prepared = new List<(int EmployeeId, double Charged, double Paid)>();
        foreach (var item in dto.Items)
        {
            if (item.Excluded) continue;
            if (item.EmployeeId <= 0) continue;
            var emp = await employeeRepository.GetByIdAsync(item.EmployeeId);
            if (emp is null || emp.BusinessId != bid) continue;

            double charged = 0;
            if (!string.IsNullOrWhiteSpace(item.ChargedAmount) &&
                double.TryParse(item.ChargedAmount.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var c) &&
                c >= 0)
            {
                charged = c;
            }

            double paid = 0;
            if (!string.IsNullOrWhiteSpace(item.AmountPaid) &&
                double.TryParse(item.AmountPaid.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var p) &&
                p >= 0)
            {
                paid = p;
            }

            if (charged <= 0 && paid <= 0) continue;
            prepared.Add((emp.Id, charged, paid));
        }

        if (prepared.Count == 0)
            return BadRequest("Nothing to record — every item was excluded or had zero amounts.");

        var alreadySalaryEmployeeIds = await dbContext.EmployeePayments
            .AsNoTracking()
            .Where(x =>
                x.BusinessId == bid &&
                !x.IsDeleted &&
                x.PeriodLabel == period &&
                x.Description == "Salary" &&
                x.ChargedAmount > 0.00001)
            .Select(x => x.EmployeeId)
            .Distinct()
            .ToListAsync();
        var dupSet = alreadySalaryEmployeeIds.ToHashSet();
        var skipped = new List<object>();
        var filtered = new List<(int EmployeeId, double Charged, double Paid)>();
        foreach (var item in prepared)
        {
            if (dupSet.Contains(item.EmployeeId))
            {
                var emp = await employeeRepository.GetByIdAsync(item.EmployeeId);
                skipped.Add(new
                {
                    employeeId = item.EmployeeId,
                    name = emp?.Name ?? $"#{item.EmployeeId}",
                    reason = "Salary already recorded for this period.",
                });
                continue;
            }
            filtered.Add(item);
        }

        if (filtered.Count == 0)
        {
            return BadRequest(
                skipped.Count > 0
                    ? $"No new payroll rows — selected employee(s) already have a salary accrual for {period}."
                    : "Nothing to record — every item was excluded or had zero amounts.");
        }

        var created = await repository.CreatePayrollRunAsync(bid, stationId, userId, period, paymentDate, filtered);
        return Ok(new
        {
            period,
            paymentDate,
            stationId,
            createdRowCount = created.Count,
            paidEmployeeCount = filtered.Count,
            totalCharged = Math.Round(filtered.Sum(p => p.Charged), 2, MidpointRounding.AwayFromZero),
            totalPaid = Math.Round(filtered.Sum(p => p.Paid), 2, MidpointRounding.AwayFromZero),
            skippedEmployeeCount = skipped.Count,
            skippedEmployees = skipped,
            rows = created,
        });
    }
}
