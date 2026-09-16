using backend.Data;
using backend.DTOs.Responses;
using backend.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace backend.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Administrador")]
public sealed class AdminController : ControllerBase
{
    private readonly AppDbContext _context;

    public AdminController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet("solicitudes")]
    public async Task<ActionResult<IReadOnlyList<TutorSolicitudDto>>> ObtenerSolicitudes(
        CancellationToken cancellationToken)
    {
        var solicitudes = await _context.Usuarios
            .AsNoTracking()
            .Where(usuario =>
                usuario.Rol == "Tutor" &&
                usuario.EstadoAprobacionId == (int)EstadoAprobacion.Pendiente)
            .OrderBy(usuario => usuario.Id)
            .Select(usuario => new TutorSolicitudDto
            {
                Id = usuario.Id,
                Nombre = usuario.Nombre,
                Correo = usuario.Email
            })
            .ToListAsync(cancellationToken);

        return Ok(solicitudes);
    }

    [HttpPut("tutores/{id:int}/aprobar")]
    public async Task<IActionResult> AprobarTutor(
        int id,
        CancellationToken cancellationToken)
    {
        var tutor = await _context.Usuarios
            .FirstOrDefaultAsync(
                usuario =>
                    usuario.Id == id &&
                    usuario.Rol == "Tutor" &&
                    usuario.EstadoAprobacionId == (int)EstadoAprobacion.Pendiente,
                cancellationToken);

        if (tutor is null)
        {
            return NotFound(new { message = "No se encontró una solicitud de tutor pendiente." });
        }

        tutor.EstadoAprobacionId = (int)EstadoAprobacion.Aprobado;
        await _context.SaveChangesAsync(cancellationToken);

        return NoContent();
    }
}
