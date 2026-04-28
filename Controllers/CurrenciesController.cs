using gas_station.Data.Interfaces;
using gas_station.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace gas_station.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CurrenciesController(ICurrencyRepository repository) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll() => Ok(await repository.GetAllAsync());

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id) => Ok(await repository.GetByIdAsync(id));

    [HttpPost]
    public async Task<IActionResult> Create(Currency model) => Ok(await repository.AddAsync(model));

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, Currency model) => Ok(await repository.UpdateAsync(id, model));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id) => Ok(await repository.DeleteAsync(id));
}
