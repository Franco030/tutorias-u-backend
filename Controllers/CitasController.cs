using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using backend.Data;
using backend.Models;
using backend.DTOs.Citas;
using backend.DTOs.Responses;
using Microsoft.EntityFrameworkCore;

namespace backend.Controllers;

[Route("api/[controller]")]
[ApiController]
public class CitasController : ControllerBase
{
    private readonly AppDbContext _context;

    public CitasController(AppDbContext context)
    {
        _context = context;
    }

    [HttpPost("agendar")]
    [Authorize(Roles = "Estudiante")]
    public async Task<IActionResult> AgendarCita([FromBody] CrearCitaRequestDto request)
    {
        if (request.FechaHoraInicio < DateTime.UtcNow)
        {
            return BadRequest("No puedes agendar en el pasado");
        }

        if (request.FechaHoraFin <= request.FechaHoraInicio ||
            request.FechaHoraFin.Date != request.FechaHoraInicio.Date)
        {
            return BadRequest("El bloque de la cita no es válido");
        }

        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int estudianteId))
        {
            return Unauthorized();
        }

        var diaSemana = (byte)request.FechaHoraInicio.DayOfWeek;
        var horaInicio = TimeOnly.FromDateTime(request.FechaHoraInicio);
        var horaFin = TimeOnly.FromDateTime(request.FechaHoraFin);
        var disponibilidadConfigurada = await _context.TutorDisponibilidads
            .AnyAsync(disponibilidad =>
                disponibilidad.TutorId == request.TutorId &&
                disponibilidad.DiaSemana == diaSemana &&
                disponibilidad.HoraInicio == horaInicio &&
                disponibilidad.HoraFin == horaFin);

        if (!disponibilidadConfigurada)
        {
            return BadRequest("El bloque solicitado no coincide con la disponibilidad del tutor");
        }

        var cita = new Cita
        {
            EstudianteId = estudianteId,
            TutorId = request.TutorId,
            MateriaId = request.MateriaId,
            FechaHoraInicio = request.FechaHoraInicio,
            Estado = "Pendiente"
        };

        _context.Citas.Add(cita);
        await _context.SaveChangesAsync();

        return Created("", new { Mensaje = "Cita agendada exitosamente", CitaId = cita.Id });
    }

    [HttpPut("{id}/aceptar")]
    [Authorize(Roles = "Tutor")]
    public async Task<IActionResult> AceptarCita(int id, [FromBody] AceptarCitaRequestDto request)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int tutorId))
        {
            return Unauthorized();
        }

        var cita = await _context.Citas.FindAsync(id);
        if (cita == null)
        {
            return NotFound();
        }

        if (cita.TutorId != tutorId)
        {
            return Forbid();
        }

        cita.Estado = "Aceptada";
        cita.LinkReunion = request.LinkReunion;
        cita.Notas = request.Notas;

        await _context.SaveChangesAsync();

        return Ok(new { Mensaje = "Cita aceptada" });
    }

    [HttpPut("{id}/rechazar")]
    [Authorize(Roles = "Tutor")]
    public async Task<IActionResult> RechazarCita(int id)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int tutorId))
        {
            return Unauthorized();
        }

        var cita = await _context.Citas.FindAsync(id);
        if (cita == null)
        {
            return NotFound();
        }

        if (cita.TutorId != tutorId)
        {
            return Forbid();
        }

        cita.Estado = "Rechazada";

        await _context.SaveChangesAsync();

        return Ok(new { Mensaje = "Cita rechazada" });
    }

    [HttpGet("pendientes")]
    [Authorize(Roles = "Tutor")]
    public async Task<ActionResult<IReadOnlyList<CitaPendienteResponseDto>>> ObtenerPendientes()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userIdClaim) ||
            !int.TryParse(userIdClaim, out int tutorId))
        {
            return Unauthorized();
        }

        var citas = await _context.Citas
            .AsNoTracking()
            .Where(cita =>
                cita.TutorId == tutorId &&
                cita.Estado == "Pendiente")
            .OrderBy(cita => cita.FechaHoraInicio)
            .Select(cita => new CitaPendienteResponseDto
            {
                Id = cita.Id,
                EstudianteId = cita.EstudianteId,
                EstudianteNombre = cita.Estudiante.Nombre,
                MateriaId = cita.MateriaId,
                MateriaNombre = cita.Materia.Nombre,
                FechaHoraInicio = cita.FechaHoraInicio,
                Estado = cita.Estado
            })
            .ToListAsync();

        return Ok(citas);
    }

    [HttpGet("proximas")]
    [Authorize]
    public async Task<IActionResult> ObtenerProximasCitas()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                       ?? User.FindFirst("sub")?.Value;

        var userRole = User.FindFirst(ClaimTypes.Role)?.Value
                    ?? User.FindFirst("role")?.Value;

        if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
        {
            return Unauthorized(new { mensaje = "Usuario no autorizado o token inválido." });
        }

        var ahoraUtc = DateTime.UtcNow;

        IQueryable<ProximaCitaResponseDto> query;

        if (userRole == "Estudiante")
        {
            query = _context.Citas
                .AsNoTracking()
                .Where(c => c.EstudianteId == userId && c.Estado == "Aceptada" && c.FechaHoraInicio >= ahoraUtc)
                .OrderBy(c => c.FechaHoraInicio)
                .Select(c => new ProximaCitaResponseDto
                {
                    Id = c.Id,
                    RolOpuestoNombre = c.Tutor.Nombre,
                    Materia = c.Materia.Nombre,
                    FechaHoraInicio = c.FechaHoraInicio,
                    LinkReunion = c.LinkReunion,
                    Notas = c.Notas
                });
        }
        else if (userRole == "Tutor")
        {
            query = _context.Citas
                .AsNoTracking()
                .Where(c => c.TutorId == userId && c.Estado == "Aceptada" && c.FechaHoraInicio >= ahoraUtc)
                .OrderBy(c => c.FechaHoraInicio)
                .Select(c => new ProximaCitaResponseDto
                {
                    Id = c.Id,
                    RolOpuestoNombre = c.Estudiante.Nombre,
                    Materia = c.Materia.Nombre,
                    FechaHoraInicio = c.FechaHoraInicio,
                    LinkReunion = c.LinkReunion,
                    Notas = c.Notas
                });
        }
        else
        {
            return Forbid();
        }

        var proximasCitas = await query.ToListAsync();
        return Ok(proximasCitas);
    }
}