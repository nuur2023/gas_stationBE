using backend.Data.Interfaces;
using backend.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace backend.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class BusinessesController(IBusinessRepository repository) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetPaged([FromQuery] int page = 1, [FromQuery] int pageSize = 50, [FromQuery] string? q = null)
        => Ok(await repository.GetPagedAsync(page, pageSize, q));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id) => Ok(await repository.GetByIdAsync(id));

    [HttpPost]
    public async Task<IActionResult> Create(Business model) => Ok(await repository.AddAsync(model));

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, Business model) => Ok(await repository.UpdateAsync(id, model));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id) => Ok(await repository.DeleteAsync(id));
}
