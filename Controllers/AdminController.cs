using backend.Data;
using backend.DTOs.Responses;
using backend.Enums;
using backend.Services.Interfaces;
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
    private readonly INotificacionesCorreo _notificaciones;

    public AdminController(AppDbContext context, INotificacionesCorreo notificaciones)
    {
        _context = context;
        _notificaciones = notificaciones;
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
            .Select(usuario => new
            {
                usuario.Id,
                usuario.Nombre,
                usuario.Email,
                Ultima = usuario.TutorSolicitudesCredenciales
                    .OrderByDescending(s => s.FechaSolicitud)
                    .Select(s => new
                    {
                        s.UrlCredencial,
                        s.FechaSolicitud,
                        Materias = s.SolicitudMateria.Select(sm => sm.Materia.Nombre).ToList()
                    })
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var resultado = solicitudes
            .Select(s => new TutorSolicitudDto
            {
                Id = s.Id,
                Nombre = s.Nombre,
                Correo = s.Email,
                UrlCredencial = s.Ultima?.UrlCredencial,
                FechaSolicitud = s.Ultima?.FechaSolicitud,
                Materias = s.Ultima?.Materias ?? new List<string>()
            })
            .ToList();

        return Ok(resultado);
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

        _notificaciones.TutorAprobado(tutor.Nombre, tutor.Email);

        return NoContent();
    }

    [HttpPut("tutores/{id:int}/rechazar")]
    public async Task<IActionResult> RechazarTutor(
        int id,
        CancellationToken cancellationToken)
    {
        var aspirante = await _context.Usuarios
            .FirstOrDefaultAsync(
                usuario =>
                    usuario.Id == id &&
                    usuario.EstadoAprobacionId == (int)EstadoAprobacion.Pendiente,
                cancellationToken);

        if (aspirante is null)
        {
            return NotFound(new { message = "No se encontró una solicitud de tutor pendiente." });
        }

        // Solo cambia el estado: el rol y los datos del usuario se conservan.
        aspirante.EstadoAprobacionId = (int)EstadoAprobacion.Rechazado;
        await _context.SaveChangesAsync(cancellationToken);

        _notificaciones.TutorRechazado(aspirante.Nombre, aspirante.Email);

        return NoContent();
    }
}
