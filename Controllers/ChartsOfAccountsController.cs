using backend.Data.Context;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace backend.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ChartsOfAccountsController(GasStationDBContext db) : ControllerBase
{
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
}

