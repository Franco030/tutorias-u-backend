using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using backend.DTOs.Requests;
using backend.Services;
using backend.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using backend.Middleware;

namespace backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IAuthService authService, ILogger<AuthController> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    [HttpPost("google")]
    public async Task<IActionResult> GoogleLogin([FromBody] ExternalLoginDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.IdToken))
        {
            return Problem(statusCode: 400, title: "El IdToken es requerido.");
        }

        try
        {
            var result = await _authService.LoginWithGoogleAsync(dto);
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Problem(statusCode: 401, title: ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fallo el inicio de sesion con Google.");
            return Problem(statusCode: 500, title: "Error al autenticar con Google.");
        }
    }

    [HttpPost("microsoft")]
    public async Task<IActionResult> MicrosoftLogin([FromBody] ExternalLoginDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.IdToken))
        {
            return Problem(statusCode: 400, title: "El IdToken es requerido.");
        }

        try
        {
            var result = await _authService.LoginWithMicrosoftAsync(dto);
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Problem(statusCode: 401, title: ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fallo el inicio de sesion con Microsoft.");
            return Problem(statusCode: 500, title: "Error al autenticar con Microsoft.");
        }
    }

    [HttpPost("register")]
    [EnableRateLimiting(LimiteAcceso.Politica)]
    public async Task<IActionResult> Register([FromBody] RegisterDto dto)
    {
        try
        {
            await _authService.RegisterAsync(dto);
            return Ok(new { message = "Registro exitoso. Por favor revisa tu correo para verificar tu cuenta." });
        }
        catch (Exception ex)
        {
            return Problem(statusCode: 400, title: ex.Message);
        }
    }

    [HttpPost("login")]
    [EnableRateLimiting(LimiteAcceso.Politica)]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {
        try
        {
            var result = await _authService.LoginWithEmailAsync(dto);
            return Ok(result);
        }
        catch (CorreoSinVerificarException ex)
        {
            // Código propio para que el cliente ofrezca reenviar el correo de verificación.
            var problema = ProblemDetailsFactory.CreateProblemDetails(HttpContext, StatusCodes.Status403Forbidden, ex.Message);
            problema.Extensions["codigo"] = "correo-sin-verificar";
            return new ObjectResult(problema) { StatusCode = StatusCodes.Status403Forbidden };
        }
        catch (UnauthorizedAccessException ex)
        {
            return Problem(statusCode: 401, title: ex.Message);
        }
        catch (Exception ex)
        {
            return Problem(statusCode: 400, title: ex.Message);
        }
    }

    [HttpGet("verify-email")]
    public async Task<IActionResult> VerifyEmail([FromQuery] string token)
    {
        try
        {
            if (string.IsNullOrEmpty(token)) return Problem(statusCode: 400, title: "Token no proporcionado.");

            await _authService.VerifyEmailAsync(token);
            return Ok(new { message = "Correo verificado exitosamente. Ya puedes iniciar sesion" });
        }
        catch (Exception ex)
        {
            return Problem(statusCode: 400, title: ex.Message);
        }
    }

    // Responde igual exista o no la cuenta: no revela qué correos están registrados.
    [HttpPost("recuperar-contrasena")]
    [EnableRateLimiting(LimiteAcceso.Politica)]
    public async Task<IActionResult> RecuperarContrasena(
        [FromBody] CorreoCuentaDto dto,
        [FromServices] RecuperacionCuenta recuperacion,
        CancellationToken cancellationToken)
    {
        await recuperacion.SolicitarRestablecimientoAsync(dto.Email, cancellationToken);
        return Ok(new { message = "Si hay una cuenta con ese correo, te enviamos un enlace para restablecer la contraseña." });
    }

    [HttpPost("restablecer-contrasena")]
    [EnableRateLimiting(LimiteAcceso.Politica)]
    public async Task<IActionResult> RestablecerContrasena(
        [FromBody] RestablecerContrasenaDto dto,
        [FromServices] RecuperacionCuenta recuperacion,
        CancellationToken cancellationToken)
    {
        try
        {
            await recuperacion.RestablecerAsync(dto.Token, dto.Password, cancellationToken);
            return Ok(new { message = "Tu contraseña cambió. Ya puedes iniciar sesión con la nueva." });
        }
        catch (InvalidOperationException ex)
        {
            return Problem(statusCode: 400, title: ex.Message);
        }
    }

    // Responde igual exista o no la cuenta, y aunque ya esté verificada.
    [HttpPost("reenviar-verificacion")]
    [EnableRateLimiting(LimiteAcceso.Politica)]
    public async Task<IActionResult> ReenviarVerificacion(
        [FromBody] CorreoCuentaDto dto,
        [FromServices] RecuperacionCuenta recuperacion,
        CancellationToken cancellationToken)
    {
        await recuperacion.ReenviarVerificacionAsync(dto.Email, cancellationToken);
        return Ok(new { message = "Si tu cuenta sigue sin verificar, te enviamos un correo nuevo." });
    }

    [HttpGet("sesion")]
    [Authorize]
    public async Task<IActionResult> RefrescarSesion()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userIdClaim == null || !int.TryParse(userIdClaim, out int userId))
            return Problem(statusCode: 401, title: "Token invalido");

        try
        {
            return Ok(await _authService.RefrescarSesionAsync(userId));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Problem(statusCode: 401, title: ex.Message);
        }
    }

    [HttpPost("asignar-rol")]
    [Authorize]
    public async Task<IActionResult> AsignarRol([FromBody] AsignarRolRequestDto dto)
    {
        try
        {
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            if (userIdClaim == null || !int.TryParse(userIdClaim, out int userId))
                return Problem(statusCode: 401, title: "Token invalido");

            var result = await _authService.AsignarRolAsync(userId, dto.NuevoRol);

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(statusCode: 409, title: ex.Message);
        }
        catch (Exception ex)
        {
            return Problem(statusCode: 400, title: ex.Message);
        }
    }
}
