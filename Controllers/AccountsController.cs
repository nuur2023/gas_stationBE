using System.Security.Claims;
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
public class AccountsController(IAccountRepository repository, GasStationDBContext db) : ControllerBase
{
    private const string SuperAdminRole = "SuperAdmin";
    private const string AdminRole = "Admin";
    private const string AccountantRole = "Accountant";

    private static bool IsSuperAdmin(ClaimsPrincipal user) => user.IsInRole(SuperAdminRole);

    /// <summary>Admin / Accountant temporary top-level business accounts (null parent, explicit chart type).</summary>
    private static bool IsAdminOrAccountant(ClaimsPrincipal user) =>
        user.IsInRole(AdminRole) || user.IsInRole(AccountantRole);

    private static bool CanSetBusinessTopLevelWithoutParent(ClaimsPrincipal user) =>
        IsSuperAdmin(user) || IsAdminOrAccountant(user);

    private bool TryGetJwtBusiness(out int businessId)
    {
        businessId = 0;
        var bidStr = User.FindFirstValue("business_id");
        return !string.IsNullOrEmpty(bidStr) && int.TryParse(bidStr, out businessId);
    }

    [HttpGet]
    public async Task<IActionResult> GetPaged([FromQuery] int page = 1, [FromQuery] int pageSize = 50, [FromQuery] string? q = null, [FromQuery] int? businessId = null)
    {
        if (IsSuperAdmin(User))
            return Ok(await repository.GetPagedAsync(page, pageSize, q, businessId));

        if (!TryGetJwtBusiness(out var bid))
            return BadRequest("No business assigned to this user.");

        return Ok(await repository.GetPagedAsync(page, pageSize, q, bid));
    }

    /// <summary>Accounts that can be parents when creating a sub-account for the given business (shared global top-level + that business’s top-level).</summary>
    [HttpGet("parent-candidates")]
    public async Task<IActionResult> GetParentCandidates([FromQuery] int businessId)
    {
        if (businessId <= 0)
            return BadRequest("businessId is required.");

        if (IsSuperAdmin(User))
            return Ok(await repository.GetParentCandidatesAsync(businessId));

        if (!TryGetJwtBusiness(out var bid))
            return BadRequest("No business assigned to this user.");
        if (businessId != bid)
            return Forbid();

        return Ok(await repository.GetParentCandidatesAsync(businessId));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var row = await repository.GetByIdAsync(id);
        if (row is null) return NotFound();
        return Ok(row);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] AccountWriteRequestViewModel dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name) || string.IsNullOrWhiteSpace(dto.Code))
            return BadRequest("Name and code are required.");

        var code = dto.Code.Trim();
        var name = dto.Name.Trim();

        int? targetBusinessId;
        if (IsSuperAdmin(User))
            targetBusinessId = dto.BusinessId is > 0 ? dto.BusinessId : null;
        else
        {
            if (!TryGetJwtBusiness(out var bid))
                return BadRequest("No business assigned to this user.");
            if (dto.BusinessId is > 0 && dto.BusinessId != bid)
                return Forbid();
            targetBusinessId = bid;
        }

        if (targetBusinessId is null)
        {
            if (!IsSuperAdmin(User))
                return Forbid();
            if (dto.ParentAccountId.HasValue)
                return BadRequest("A global parent account cannot have a parent.");
            if (dto.ChartsOfAccountsId <= 0)
                return BadRequest("Select a type (chart of accounts).");

            var chart = await db.ChartsOfAccounts.FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == dto.ChartsOfAccountsId);
            if (chart is null)
                return BadRequest("Invalid chart of account.");

            var dup = await repository.GetByBusinessAndCodeAsync(null, code);
            if (dup is not null) return BadRequest("Account code already exists for global accounts.");

            var entity = new Account
            {
                BusinessId = null,
                Name = name,
                Code = code,
                ChartsOfAccountsId = dto.ChartsOfAccountsId,
                ParentAccountId = null,
            };
            return Ok(await repository.AddAsync(entity));
        }

        int chartsOfAccountsId;
        int? parentAccountId = null;
        if (dto.ParentAccountId.HasValue)
        {
            var parent = await repository.GetByIdAsync(dto.ParentAccountId.Value);
            if (parent is null) return BadRequest("Invalid parent account.");
            if (parent.ParentAccountId != null)
                return BadRequest("Parent must be a top-level account.");
            if (parent.BusinessId is not null && parent.BusinessId != targetBusinessId)
                return BadRequest("Parent must be a global top-level account or belong to the selected business.");

            chartsOfAccountsId = parent.ChartsOfAccountsId;
            parentAccountId = parent.Id;
        }
        else
        {
            if (!CanSetBusinessTopLevelWithoutParent(User))
                return BadRequest("Select a parent account for this business.");
            if (dto.ChartsOfAccountsId <= 0)
                return BadRequest("Select a type (chart of accounts) when no parent is selected.");

            var chart = await db.ChartsOfAccounts.FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == dto.ChartsOfAccountsId);
            if (chart is null)
                return BadRequest("Invalid chart of account.");

            chartsOfAccountsId = chart.Id;
        }

        var exists = await repository.GetByBusinessAndCodeAsync(targetBusinessId, code);
        if (exists is not null) return BadRequest("Account code already exists in this business.");

        var sub = new Account
        {
            BusinessId = targetBusinessId,
            Name = name,
            Code = code,
            ChartsOfAccountsId = chartsOfAccountsId,
            ParentAccountId = parentAccountId,
        };
        return Ok(await repository.AddAsync(sub));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] AccountWriteRequestViewModel dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name) || string.IsNullOrWhiteSpace(dto.Code))
            return BadRequest("Name and code are required.");
        var trimmedCode = dto.Code.Trim();
        var trimmedName = dto.Name.Trim();

        var existing = await repository.GetByIdAsync(id);
        if (existing is null) return NotFound();

        if (!IsSuperAdmin(User))
        {
            if (!TryGetJwtBusiness(out var bid))
                return BadRequest("No business assigned to this user.");
            if (existing.BusinessId != bid)
                return Forbid();
        }

        if (existing.BusinessId is null)
        {
            if (!IsSuperAdmin(User))
                return Forbid();
            if (dto.ParentAccountId.HasValue)
                return BadRequest("A global parent account cannot have a parent.");
            if (dto.ChartsOfAccountsId <= 0)
                return BadRequest("Select a type.");

            var chart = await db.ChartsOfAccounts.FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == dto.ChartsOfAccountsId);
            if (chart is null)
                return BadRequest("Invalid chart of account.");

            if (!string.Equals(existing.Code, trimmedCode, StringComparison.OrdinalIgnoreCase))
            {
                var byCode = await repository.GetByBusinessAndCodeAsync(null, trimmedCode);
                if (byCode is not null && byCode.Id != id) return BadRequest("Account code already exists for global accounts.");
            }

            existing.Name = trimmedName;
            existing.Code = trimmedCode;
            existing.ChartsOfAccountsId = dto.ChartsOfAccountsId;
            existing.ParentAccountId = null;
            return Ok(await repository.UpdateAsync(id, existing));
        }

        int chartsOfAccountsId;
        int? parentAccountId = null;
        if (dto.ParentAccountId.HasValue)
        {
            if (dto.ParentAccountId == id) return BadRequest("Account cannot be its own parent.");

            var newParent = await repository.GetByIdAsync(dto.ParentAccountId.Value);
            if (newParent is null) return BadRequest("Invalid parent account.");
            if (newParent.ParentAccountId != null)
                return BadRequest("Parent must be a top-level account.");
            if (newParent.BusinessId is not null && newParent.BusinessId != existing.BusinessId)
                return BadRequest("Parent must be a global top-level account or belong to this business.");

            chartsOfAccountsId = newParent.ChartsOfAccountsId;
            parentAccountId = newParent.Id;
        }
        else
        {
            if (!CanSetBusinessTopLevelWithoutParent(User))
                return BadRequest("Select a parent account.");
            if (dto.ChartsOfAccountsId <= 0)
                return BadRequest("Select a type (chart of accounts) when no parent is selected.");

            var chart = await db.ChartsOfAccounts.FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == dto.ChartsOfAccountsId);
            if (chart is null)
                return BadRequest("Invalid chart of account.");

            chartsOfAccountsId = chart.Id;
        }

        if (!string.Equals(existing.Code, trimmedCode, StringComparison.OrdinalIgnoreCase))
        {
            var byCode = await repository.GetByBusinessAndCodeAsync(existing.BusinessId, trimmedCode);
            if (byCode is not null && byCode.Id != id) return BadRequest("Account code already exists in this business.");
        }

        existing.Name = trimmedName;
        existing.Code = trimmedCode;
        existing.ChartsOfAccountsId = chartsOfAccountsId;
        existing.ParentAccountId = parentAccountId;
        return Ok(await repository.UpdateAsync(id, existing));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var existing = await repository.GetByIdAsync(id);
        if (existing is null) return NotFound();
        return Ok(await repository.DeleteAsync(id));
    }

    [HttpPost("auto-generate-default-parents")]
    public async Task<IActionResult> AutoGenerateDefaultParents()
    {
        if (!IsSuperAdmin(User))
        {
            return Forbid();
        }

        var defaults = new (string code, string name, string type)[]
        {
            ("1000", "Assets", "Asset"),
            ("2000", "Liabilities", "Liability"),
            ("3000", "Equity", "Equity"),
            ("4000", "Income", "Income"),
            ("5000", "Expenses", "Expense"),
            ("6000", "COGS", "COGS"),
        };

        var created = 0;
        foreach (var d in defaults)
        {
            var typeNorm = d.type.Trim().ToLowerInvariant();
            var chart = await db.ChartsOfAccounts.FirstOrDefaultAsync(x =>
                !x.IsDeleted && x.Type.ToLower() == typeNorm);
            if (chart is null)
            {
                chart = new ChartsOfAccounts
                {
                    Type = d.type,
                };
                db.ChartsOfAccounts.Add(chart);
                await db.SaveChangesAsync();
            }

            var exists = await repository.GetByBusinessAndCodeAsync(null, d.code);
            if (exists is not null)
            {
                // Keep defaults aligned if type mapping changed (e.g. 6000 COGS from Expense -> COGS).
                if (exists.ChartsOfAccountsId != chart.Id)
                {
                    exists.ChartsOfAccountsId = chart.Id;
                    await repository.UpdateAsync(exists.Id, exists);
                }
                continue;
            }

            await repository.AddAsync(new Account
            {
                BusinessId = null,
                Name = d.name,
                Code = d.code,
                ChartsOfAccountsId = chart.Id,
                ParentAccountId = null,
            });
            created++;
        }

        return Ok(new { created, totalDefaults = defaults.Length });
    }
}
