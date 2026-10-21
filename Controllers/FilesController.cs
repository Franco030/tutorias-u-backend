using backend.DTOs.Requests;
using backend.DTOs.Responses;
using backend.Services;
using backend.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace backend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class FilesController : ControllerBase
    {
        private readonly IBlobService _blobService;

        private readonly ILogger<FilesController> _logger;

        public FilesController(IBlobService blobService, ILogger<FilesController> logger)
        {
            _blobService = blobService;
            _logger = logger;
        }

        [HttpPost("upload")]
        // Un poco más que el máximo del archivo para el resto del formulario.
        [RequestSizeLimit(ValidadorArchivos.TamanoMaximo + 64 * 1024)]
        public async Task<ActionResult<FileUploadResponseDto>> UploadFile([FromForm] FileUploadRequestDto request)
        {
            try
            {
                var fileUrl = await _blobService.SubirComprobanteAsync(request.File);

                return Ok(new FileUploadResponseDto { Url = fileUrl });
            }
            catch (ArgumentException ex)
            {
                return Problem(statusCode: 400, title: ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falló la subida del comprobante.");
                return Problem(statusCode: 500, title: "No se pudo subir el archivo. Intenta nuevamente.");
            }
        }
    }
}
