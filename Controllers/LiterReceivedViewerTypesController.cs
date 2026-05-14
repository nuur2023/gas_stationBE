using gas_station.Data.Context;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace gas_station.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class LiterReceivedViewerTypesController(GasStationDBContext dbContext) : ControllerBase
{
    /// <summary>All non-deleted viewer types for liter-received forms (ordered by name).</summary>
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var rows = await dbContext.LiterReceivedViewerTypes
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Name, x.Code })
            .ToListAsync();
        return Ok(rows);
    }
}
