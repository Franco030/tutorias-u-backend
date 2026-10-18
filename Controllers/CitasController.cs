using System;
using System.Collections.Generic;
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
    public async Task<IActionResult> AgendarCita(
        [FromBody] CrearCitaRequestDto request,
        CancellationToken cancellationToken = default)
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

        var compromisoExistente = await _context.Citas.AnyAsync(cita =>
            cita.FechaHoraInicio == request.FechaHoraInicio &&
            cita.Estado != "Cancelada" &&
            cita.Estado != "Rechazada" &&
            (cita.TutorId == request.TutorId || cita.EstudianteId == estudianteId),
            cancellationToken);

        if (compromisoExistente)
        {
            return BadRequest("El tutor ya tiene un compromiso en este horario");
        }

        var diaSemana = (byte)request.FechaHoraInicio.DayOfWeek;
        var horaInicio = TimeOnly.FromDateTime(request.FechaHoraInicio);
        var horaFin = TimeOnly.FromDateTime(request.FechaHoraFin);
        var disponibilidadConfigurada = await _context.TutorDisponibilidads
            .AnyAsync(disponibilidad =>
                disponibilidad.TutorId == request.TutorId &&
                disponibilidad.DiaSemana == diaSemana &&
                disponibilidad.HoraInicio == horaInicio &&
                disponibilidad.HoraFin == horaFin,
                cancellationToken);

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
        await _context.SaveChangesAsync(cancellationToken);

        return Created("", new { Mensaje = "Cita agendada exitosamente", CitaId = cita.Id });
    }

    [HttpPut("{id}/aceptar")]
    [Authorize(Roles = "Tutor")]
    public async Task<IActionResult> AceptarCita(
        int id,
        [FromBody] AceptarCitaRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int tutorId))
        {
            return Unauthorized();
        }

        var cita = await _context.Citas.FindAsync([id], cancellationToken);
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

        await _context.SaveChangesAsync(cancellationToken);

        return Ok(new { Mensaje = "Cita aceptada" });
    }

    [HttpPut("{id}/rechazar")]
    [Authorize(Roles = "Tutor")]
    public async Task<IActionResult> RechazarCita(int id, CancellationToken cancellationToken = default)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int tutorId))
        {
            return Unauthorized();
        }

        var cita = await _context.Citas.FindAsync([id], cancellationToken);
        if (cita == null)
        {
            return NotFound();
        }

        if (cita.TutorId != tutorId)
        {
            return Forbid();
        }

        cita.Estado = "Rechazada";

        await _context.SaveChangesAsync(cancellationToken);

        return Ok(new { Mensaje = "Cita rechazada" });
    }

    [HttpGet("pendientes")]
    [Authorize(Roles = "Tutor")]
    public async Task<ActionResult<IReadOnlyList<CitaPendienteResponseDto>>> ObtenerPendientes(
        CancellationToken cancellationToken = default)
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
            .ToListAsync(cancellationToken);

        return Ok(citas);
    }

    [HttpGet("proximas")]
    [Authorize]
    public async Task<IActionResult> ObtenerProximasCitas(CancellationToken cancellationToken = default)
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
                .Where(c => c.EstudianteId == userId &&
                            (c.Estado == "Aceptada" || c.Estado == "Pendiente") &&
                            c.FechaHoraInicio >= ahoraUtc)
                .OrderBy(c => c.FechaHoraInicio)
                .Select(c => new ProximaCitaResponseDto
                {
                    Id = c.Id,
                    RolOpuestoNombre = c.Tutor.Nombre,
                    Materia = c.Materia.Nombre,
                    FechaHoraInicio = c.FechaHoraInicio,
                    LinkReunion = c.LinkReunion,
                    Notas = c.Notas,
                    Estado = c.Estado
                });
        }
        else if (userRole == "Tutor")
        {
            query = _context.Citas
                .AsNoTracking()
                .Where(c => c.TutorId == userId &&
                            (c.Estado == "Aceptada" || c.Estado == "Pendiente") &&
                            c.FechaHoraInicio >= ahoraUtc)
                .OrderBy(c => c.FechaHoraInicio)
                .Select(c => new ProximaCitaResponseDto
                {
                    Id = c.Id,
                    RolOpuestoNombre = c.Estudiante.Nombre,
                    Materia = c.Materia.Nombre,
                    FechaHoraInicio = c.FechaHoraInicio,
                    LinkReunion = c.LinkReunion,
                    Notas = c.Notas,
                    Estado = c.Estado
                });
        }
        else
        {
            return Forbid();
        }

        var proximasCitas = await query.ToListAsync(cancellationToken);
        return Ok(proximasCitas);
    }

    [HttpGet("ocupadas")]
    [Authorize]
    public async Task<ActionResult<IReadOnlyList<string>>> ObtenerHorasOcupadas(
        [FromQuery] int tutorId,
        [FromQuery] DateOnly fecha,
        CancellationToken cancellationToken = default)
    {
        var fechaInicio = fecha.ToDateTime(TimeOnly.MinValue);
        var fechaFin = fechaInicio.AddDays(1);
        var citasOcupadas = await _context.Citas
            .AsNoTracking()
            .Where(cita =>
                cita.TutorId == tutorId &&
                cita.FechaHoraInicio >= fechaInicio &&
                cita.FechaHoraInicio < fechaFin &&
                cita.Estado != "Cancelada" &&
                cita.Estado != "Rechazada")
            .OrderBy(cita => cita.FechaHoraInicio)
            .Select(cita => cita.FechaHoraInicio)
            .ToListAsync(cancellationToken);

        return Ok(citasOcupadas.Select(hora => hora.ToString("HH:mm")).ToArray());
    }

    [HttpPut("{id}/cancelar")]
    [Authorize]
    public async Task<IActionResult> CancelarCita(
        int id,
        [FromBody] CancelarCitaRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                       ?? User.FindFirst("sub")?.Value
                       ?? User.FindFirst("id")?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized();
        }

        var cita = await _context.Citas.FindAsync([id], cancellationToken);
        if (cita == null)
        {
            return NotFound();
        }

        if (cita.TutorId != userId && cita.EstudianteId != userId)
        {
            return Forbid();
        }

        if (cita.Estado != "Pendiente" && cita.Estado != "Aceptada")
        {
            return BadRequest("Solo se pueden cancelar citas pendientes o aceptadas");
        }

        if (!string.IsNullOrWhiteSpace(request.Motivo))
        {
            cita.Notas = string.IsNullOrWhiteSpace(cita.Notas)
                ? $"Motivo de cancelación: {request.Motivo.Trim()}"
                : $"{cita.Notas}{Environment.NewLine}Motivo de cancelación: {request.Motivo.Trim()}";
        }

        cita.Estado = "Cancelada";
        await _context.SaveChangesAsync(cancellationToken);

        return Ok(new { Mensaje = "Cita cancelada" });
    }

    [HttpGet("historial")]
    [Authorize]
    public async Task<IActionResult> ObtenerHistorial(CancellationToken cancellationToken = default)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                       ?? User.FindFirst("sub")?.Value
                       ?? User.FindFirst("id")?.Value;
        var userRole = User.FindFirst(ClaimTypes.Role)?.Value
                    ?? User.FindFirst("role")?.Value;

        if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized();
        }

        var ahoraUtc = DateTime.UtcNow;
        var citasQuery = _context.Citas
            .Where(cita =>
                (userRole == "Estudiante" && cita.EstudianteId == userId) ||
                (userRole == "Tutor" && cita.TutorId == userId));

        var citasCompletadas = await citasQuery
            .Where(cita => cita.Estado == "Aceptada" && cita.FechaHoraInicio < ahoraUtc)
            .ToListAsync(cancellationToken);

        foreach (var cita in citasCompletadas)
        {
            cita.Estado = "Completada";
        }

        if (citasCompletadas.Count > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        var historial = await citasQuery
            .AsNoTracking()
            .Where(cita =>
                cita.FechaHoraInicio < ahoraUtc ||
                cita.Estado == "Completada" ||
                cita.Estado == "Cancelada" ||
                cita.Estado == "Rechazada")
            .OrderByDescending(cita => cita.FechaHoraInicio)
            .Select(cita => new ProximaCitaResponseDto
            {
                Id = cita.Id,
                RolOpuestoNombre = userRole == "Estudiante" ? cita.Tutor.Nombre : cita.Estudiante.Nombre,
                Materia = cita.Materia.Nombre,
                FechaHoraInicio = cita.FechaHoraInicio,
                LinkReunion = cita.LinkReunion,
                Notas = cita.Notas,
                Estado = cita.Estado
            })
            .ToListAsync(cancellationToken);

        return userRole == "Estudiante" || userRole == "Tutor"
            ? Ok(historial)
            : Forbid();
    }

    [HttpPut("{id}/completar")]
    [Authorize(Roles = "Tutor")]
    public async Task<IActionResult> CompletarCita(int id, CancellationToken cancellationToken = default)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                       ?? User.FindFirst("sub")?.Value
                       ?? User.FindFirst("id")?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var tutorId))
        {
            return Unauthorized();
        }

        var cita = await _context.Citas.FindAsync([id], cancellationToken);
        if (cita == null)
        {
            return NotFound();
        }

        if (cita.TutorId != tutorId)
        {
            return Forbid();
        }

        if (cita.Estado != "Aceptada")
        {
            return BadRequest("Solo se pueden completar citas aceptadas");
        }

        cita.Estado = "Completada";
        await _context.SaveChangesAsync(cancellationToken);
        return Ok(new { Mensaje = "Cita completada" });
    }
}