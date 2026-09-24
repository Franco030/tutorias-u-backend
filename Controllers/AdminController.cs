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
            .Include(u => u.TutorSolicitudesCredenciales)
                .ThenInclude(s => s.SolicitudMateria)
            .Include(u => u.MateriaNavigation)
            .FirstOrDefaultAsync(
                usuario =>
                    usuario.Id == id &&
                    usuario.EstadoAprobacionId == (int)EstadoAprobacion.Pendiente,
                cancellationToken);

        if (tutor is null)
        {
            return NotFound(new { message = "No se encontró una solicitud de tutor pendiente." });
        }

        tutor.EstadoAprobacionId = (int)EstadoAprobacion.Aprobado;
        tutor.Rol = "Tutor";

        // Obtener la solicitud más reciente
        var ultimaSolicitud = tutor.TutorSolicitudesCredenciales
            .OrderByDescending(s => s.FechaSolicitud)
            .FirstOrDefault();

        if (ultimaSolicitud != null)
        {
            // Migrar materias de la solicitud al catálogo activo del tutor
            foreach (var solMat in ultimaSolicitud.SolicitudMateria)
            {
                var materia = await _context.Materias.FindAsync(new object[] { solMat.MateriaId }, cancellationToken);
                if (materia != null && !tutor.MateriaNavigation.Any(m => m.Id == materia.Id))
                {
                    tutor.MateriaNavigation.Add(materia);
                }
            }
        }

        await _context.SaveChangesAsync(cancellationToken);

        return NoContent();
    }
}
