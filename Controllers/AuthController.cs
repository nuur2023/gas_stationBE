using backend.Data.Interfaces;
using backend.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace backend.Controllers;

[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public class AuthController(IAuthRepository authRepository) : ControllerBase
{
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequestViewModel model)
    {
        var result = await authRepository.LoginAsync(model);
        if (result is null)
        {
            return Unauthorized("Invalid email, phone, or password.");
        }

        return Ok(result);
    }
}
