using Microsoft.AspNetCore.Mvc;
using backend.DTOs.Requests;
using backend.Services.Interfaces;

namespace backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("google")]
    public async Task<IActionResult> GoogleLogin([FromBody] ExternalLoginDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.IdToken))
        {
            return BadRequest(new { message = "El IdToken es requerido." });
        }

        try
        {
            var result = await _authService.LoginWithGoogleAsync(dto);
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Error al autenticar con Google.", details = ex.Message });
        }
    }

    [HttpPost("microsoft")]
    public async Task<IActionResult> MicrosoftLogin([FromBody] ExternalLoginDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.IdToken))
        {
            return BadRequest(new { message = "El IdToken es requerido." });
        }

        try
        {
            var result = await _authService.LoginWithMicrosoftAsync(dto);
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Error al autenticar con Microsoft.", details = ex.Message });
        }
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterDto dto)
    {
        try
        {
            await _authService.RegisterAsync(dto);
            return Ok(new { message = "Registro exitoso. Por favor revisa tu correo para verificar tu cuenta." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {
        try
        {
            var result = await _authService.LoginWithEmailAsync(dto);
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("verify-email")]
    public async Task<IActionResult> VerifyEmail([FromQuery] string token)
    {
        try
        {
            if (string.IsNullOrEmpty(token)) return BadRequest("Token no proporcionado.");

            await _authService.VerifyEmailAsync(token);
            return Ok(new { message = "Correo verificado exitosamente. Ya puedes iniciar sesion" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
