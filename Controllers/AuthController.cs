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
    }
