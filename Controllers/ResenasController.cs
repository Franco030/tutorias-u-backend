using System.Security.Claims;
using backend.Data;
using backend.DTOs.Resenas;
using backend.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace backend.Controllers;

[Route("api/[controller]")]
[ApiController]
public class ResenasController : ControllerBase
{
    private const int TamanoPaginaMaximo = 50;

    private readonly AppDbContext _context;

    public ResenasController(AppDbContext context)
    {
        _context = context;
    }

    [HttpPost]
    [Authorize(Roles = "Estudiante")]
    public async Task<IActionResult> CrearResena(
        [FromBody] CrearResenaRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var estudianteId))
        {
            return Unauthorized();
        }

        var cita = await _context.Citas.FindAsync([request.CitaId], cancellationToken);
        if (cita == null)
        {
            return NotFound(new { mensaje = "La tutoría no existe." });
        }

        if (cita.EstudianteId != estudianteId)
        {
            return Forbid();
        }

        if (cita.Estado != "Completada")
        {
            return BadRequest(new { mensaje = "Solo puedes calificar tutorías completadas." });
        }

        var yaCalificada = await _context.Resenas.AnyAsync(resena => resena.CitaId == cita.Id, cancellationToken);
        if (yaCalificada)
        {
            return Conflict(new { mensaje = "Ya calificaste esta tutoría." });
        }

        var resena = new Resena
        {
            CitaId = cita.Id,
            EstudianteId = estudianteId,
            TutorId = cita.TutorId,
            Puntuacion = request.Puntuacion,
            Comentario = string.IsNullOrWhiteSpace(request.Comentario) ? null : request.Comentario.Trim(),
            FechaCreacion = DateTime.UtcNow
        };

        _context.Resenas.Add(resena);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Dos envíos simultáneos: el índice único de CitaId rechaza el segundo.
            return Conflict(new { mensaje = "Ya calificaste esta tutoría." });
        }

        var promedio = await _context.Resenas
            .Where(r => r.TutorId == cita.TutorId)
            .AverageAsync(r => (decimal)r.Puntuacion, cancellationToken);

        var tutor = await _context.Usuarios.FindAsync([cita.TutorId], cancellationToken);
        if (tutor != null)
        {
            tutor.CalificacionPromedio = Math.Round(promedio, 2);
            await _context.SaveChangesAsync(cancellationToken);
        }

        var estudiante = await _context.Usuarios
            .AsNoTracking()
            .Where(usuario => usuario.Id == estudianteId)
            .Select(usuario => new { usuario.Nombre, usuario.FotoUrl })
            .FirstOrDefaultAsync(cancellationToken);

        return Created($"/api/tutores/{cita.TutorId}/resenas", new
        {
            Resena = new ResenaResponseDto
            {
                Id = resena.Id,
                Puntuacion = resena.Puntuacion,
                Comentario = resena.Comentario,
                FechaCreacion = resena.FechaCreacion,
                EstudianteNombre = estudiante?.Nombre ?? string.Empty,
                EstudianteFotoUrl = estudiante?.FotoUrl
            },
            CalificacionPromedio = tutor?.CalificacionPromedio
        });
    }

    [HttpGet("~/api/tutores/{tutorId:int}/resenas")]
    [Authorize]
    public async Task<ActionResult<ResenasTutorResponseDto>> ObtenerResenasTutor(
        int tutorId,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamanoPagina = 10,
        CancellationToken cancellationToken = default)
    {
        var tutorExiste = await _context.Usuarios
            .AnyAsync(usuario => usuario.Id == tutorId && usuario.Rol == "Tutor", cancellationToken);
        if (!tutorExiste)
        {
            return NotFound(new { mensaje = "Tutor no encontrado." });
        }

        pagina = Math.Max(1, pagina);
        tamanoPagina = Math.Clamp(tamanoPagina, 1, TamanoPaginaMaximo);

        var resenasTutor = _context.Resenas
            .AsNoTracking()
            .Where(resena => resena.TutorId == tutorId);

        var conteos = await resenasTutor
            .GroupBy(resena => resena.Puntuacion)
            .Select(grupo => new { Puntuacion = grupo.Key, Cantidad = grupo.Count() })
            .ToListAsync(cancellationToken);

        var total = conteos.Sum(conteo => conteo.Cantidad);
        var promedio = total == 0
            ? 0m
            : Math.Round((decimal)conteos.Sum(conteo => conteo.Puntuacion * conteo.Cantidad) / total, 2);

        var desglose = Enumerable.Range(1, 5)
            .Reverse()
            .Select(puntuacion => new DesgloseResenaDto(
                puntuacion,
                conteos.FirstOrDefault(conteo => conteo.Puntuacion == puntuacion)?.Cantidad ?? 0))
            .ToList();

        var items = await resenasTutor
            .OrderByDescending(resena => resena.FechaCreacion)
            .ThenByDescending(resena => resena.Id)
            .Skip((pagina - 1) * tamanoPagina)
            .Take(tamanoPagina)
            .Select(resena => new ResenaResponseDto
            {
                Id = resena.Id,
                Puntuacion = resena.Puntuacion,
                Comentario = resena.Comentario,
                FechaCreacion = resena.FechaCreacion,
                EstudianteNombre = resena.Estudiante.Nombre,
                EstudianteFotoUrl = resena.Estudiante.FotoUrl
            })
            .ToListAsync(cancellationToken);

        return Ok(new ResenasTutorResponseDto
        {
            Items = items,
            Pagina = pagina,
            TamanoPagina = tamanoPagina,
            Total = total,
            Promedio = promedio,
            Desglose = desglose
        });
    }
}
