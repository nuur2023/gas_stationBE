using gas_station.Data.Context;
using gas_station.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace gas_station.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ChartsOfAccountsController(GasStationDBContext db) : ControllerBase
{
    private const string SuperAdminRole = "SuperAdmin";

    public sealed class ChartsOfAccountsWriteDto
    {
        public string Type { get; set; } = string.Empty;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var rows = await db.ChartsOfAccounts
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.Type)
            .ThenBy(x => x.Id)
            .ToListAsync();
        return Ok(rows);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ChartsOfAccountsWriteDto dto)
    {
        if (!User.IsInRole(SuperAdminRole)) return Forbid();
        var type = dto.Type?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(type)) return BadRequest("Type is required.");
        var exists = await db.ChartsOfAccounts.AnyAsync(x => !x.IsDeleted && x.Type.ToLower() == type.ToLower());
        if (exists) return BadRequest("Type already exists.");

        var now = DateTime.UtcNow;
        var row = new ChartsOfAccounts
        {
            Type = type,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.ChartsOfAccounts.Add(row);
        await db.SaveChangesAsync();
        return Ok(row);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] ChartsOfAccountsWriteDto dto)
    {
        if (!User.IsInRole(SuperAdminRole)) return Forbid();
        var row = await db.ChartsOfAccounts.FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id);
        if (row is null) return NotFound();

        var type = dto.Type?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(type)) return BadRequest("Type is required.");
        var exists = await db.ChartsOfAccounts.AnyAsync(x => !x.IsDeleted && x.Id != id && x.Type.ToLower() == type.ToLower());
        if (exists) return BadRequest("Type already exists.");

        row.Type = type;
        row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(row);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (!User.IsInRole(SuperAdminRole)) return Forbid();
        var row = await db.ChartsOfAccounts
            .Include(x => x.Accounts)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id);
        if (row is null) return NotFound();
        if (row.Accounts.Any(a => !a.IsDeleted)) return BadRequest("Cannot delete type that is used by accounts.");

        row.IsDeleted = true;
        row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok();
    }
}

