using gas_station.Data.Interfaces;
using gas_station.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace gas_station.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class MenusController(IMenuRepository repository) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetPaged([FromQuery] int page = 1, [FromQuery] int pageSize = 50, [FromQuery] string? q = null)
        => Ok(await repository.GetPagedAsync(page, pageSize, q));

    [HttpGet("tree")]
    public async Task<IActionResult> GetTree() => Ok(await repository.GetTreeAsync());

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id) => Ok(await repository.GetByIdAsync(id));

    [HttpPost]
    public async Task<IActionResult> Create(Menu model) => Ok(await repository.AddAsync(model));

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, Menu model) => Ok(await repository.UpdateAsync(id, model));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id) => Ok(await repository.DeleteAsync(id));
}
