using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using backend.Authorization;
using backend.Data;
using backend.Enums;
using backend.Models;
using backend.DTOs.Citas;
using backend.DTOs.Responses;
using backend.Services.Interfaces;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;

namespace backend.Controllers;

[Route("api/[controller]")]
[ApiController]
public class CitasController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly INotificacionesCorreo _notificaciones;
    private readonly ILogger<CitasController> _logger;
    private readonly IHoraPlataforma _hora;

    public CitasController(
        AppDbContext context,
        INotificacionesCorreo notificaciones,
        ILogger<CitasController> logger,
        IHoraPlataforma hora)
    {
        _context = context;
        _notificaciones = notificaciones;
        _logger = logger;
        _hora = hora;
    }

    // Avisa por correo después de guardar. Nunca lanza: si falla la consulta de
    // los datos, el cambio ya está en la base de datos y solo se registra.
    private async Task NotificarAsync(
        int citaId,
        Action<DatosCitaCorreo> enviar,
        CancellationToken cancellationToken)
    {
        try
        {
            var datos = await _context.Citas
                .AsNoTracking()
                .Where(cita => cita.Id == citaId)
                .Select(cita => new DatosCitaCorreo(
                    cita.Materia.Nombre,
                    cita.FechaHoraInicio,
                    cita.Estudiante.Nombre,
                    cita.Estudiante.Email,
                    cita.Tutor.Nombre,
                    cita.Tutor.Email))
                .FirstOrDefaultAsync(cancellationToken);

            if (datos != null)
            {
                enviar(datos);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo notificar por correo el cambio de la cita {CitaId}.", citaId);
        }
    }

    [HttpPost("agendar")]
    [Authorize(Roles = "Estudiante")]
    public async Task<IActionResult> AgendarCita(
        [FromBody] CrearCitaRequestDto request,
        CancellationToken cancellationToken = default)
    {
        // Las horas llegan en la hora local de la plataforma; si un cliente
        // antiguo las manda con "Z", se toman igual como hora local.
        var fechaHoraInicio = DateTime.SpecifyKind(request.FechaHoraInicio, DateTimeKind.Unspecified);
        var fechaHoraFin = DateTime.SpecifyKind(request.FechaHoraFin, DateTimeKind.Unspecified);

        if (fechaHoraInicio < _hora.Ahora)
        {
            return Problem(statusCode: 400, title: "No puedes agendar en el pasado");
        }

        if (fechaHoraFin <= fechaHoraInicio ||
            fechaHoraFin.Date != fechaHoraInicio.Date)
        {
            return Problem(statusCode: 400, title: "El bloque de la cita no es válido");
        }

        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int estudianteId))
        {
            return Problem(statusCode: 401, title: "Token inválido.");
        }

        var compromisoExistente = await _context.Citas.AnyAsync(cita =>
            cita.FechaHoraInicio == fechaHoraInicio &&
            cita.Estado != "Cancelada" &&
            cita.Estado != "Rechazada" &&
            (cita.TutorId == request.TutorId || cita.EstudianteId == estudianteId),
            cancellationToken);

        if (compromisoExistente)
        {
            return Problem(statusCode: 400, title: "El tutor ya tiene un compromiso en este horario");
        }

        // Solo con un tutor aprobado y en una materia que imparte.
        var tutor = await _context.Usuarios
            .AsNoTracking()
            .Where(usuario => usuario.Id == request.TutorId)
            .Select(usuario => new
            {
                usuario.Rol,
                usuario.EstadoAprobacionId,
                ImparteMateria = usuario.MateriaNavigation.Any(materia => materia.Id == request.MateriaId)
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (tutor == null || tutor.Rol != "Tutor" || tutor.EstadoAprobacionId != (int)EstadoAprobacion.Aprobado)
        {
            return Problem(statusCode: 400, title: "El tutor no está disponible para agendar.");
        }

        if (!tutor.ImparteMateria)
        {
            return Problem(statusCode: 400, title: "El tutor no imparte esa materia.");
        }

        var diaSemana = (byte)fechaHoraInicio.DayOfWeek;
        var horaInicio = TimeOnly.FromDateTime(fechaHoraInicio);
        var horaFin = TimeOnly.FromDateTime(fechaHoraFin);
        var disponibilidadConfigurada = await _context.TutorDisponibilidads
            .AnyAsync(disponibilidad =>
                disponibilidad.TutorId == request.TutorId &&
                disponibilidad.DiaSemana == diaSemana &&
                disponibilidad.HoraInicio == horaInicio &&
                disponibilidad.HoraFin == horaFin,
                cancellationToken);

        if (!disponibilidadConfigurada)
        {
            return Problem(statusCode: 400, title: "El bloque solicitado no coincide con la disponibilidad del tutor");
        }

        var cita = new Cita
        {
            EstudianteId = estudianteId,
            TutorId = request.TutorId,
            MateriaId = request.MateriaId,
            FechaHoraInicio = fechaHoraInicio,
            Estado = "Pendiente"
        };

        _context.Citas.Add(cita);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Dos reservas simultáneas del mismo bloque: el índice único
            // UX_Citas_Tutor_Horario rechaza la segunda.
            return Problem(statusCode: 409, title: "El tutor ya tiene un compromiso en este horario");
        }

        await NotificarAsync(cita.Id, _notificaciones.CitaSolicitada, cancellationToken);

        return Created("", new { Mensaje = "Cita agendada exitosamente", CitaId = cita.Id });
    }

    [HttpPut("{id}/aceptar")]
    [Authorize(Policy = PoliticasAutorizacion.TutorAprobado)]
    public async Task<IActionResult> AceptarCita(
        int id,
        [FromBody] AceptarCitaRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int tutorId))
        {
            return Problem(statusCode: 401, title: "Token inválido.");
        }

        var cita = await _context.Citas.FindAsync([id], cancellationToken);
        if (cita == null)
        {
            return Problem(statusCode: 404, title: "La cita no existe.");
        }

        if (cita.TutorId != tutorId)
        {
            return Forbid();
        }

        // Solo se acepta una solicitud pendiente y que todavía no ocurre: una
        // cancelada o rechazada no se puede reactivar (su horario pudo ocuparse).
        if (cita.Estado != "Pendiente")
        {
            return Problem(statusCode: 409, title: "Solo se pueden aceptar solicitudes pendientes.");
        }

        if (cita.FechaHoraInicio < _hora.Ahora)
        {
            return Problem(statusCode: 409, title: "La hora de esta solicitud ya pasó.");
        }

        var linkReunion = request.LinkReunion?.Trim();
        if (!string.IsNullOrEmpty(linkReunion) && !EsEnlaceWeb(linkReunion))
        {
            return Problem(statusCode: 400, title: "El enlace de reunión debe ser una dirección web (https://…).");
        }

        cita.Estado = "Aceptada";
        cita.LinkReunion = string.IsNullOrEmpty(linkReunion) ? null : linkReunion;
        cita.Notas = request.Notas;

        await _context.SaveChangesAsync(cancellationToken);

        await NotificarAsync(
            cita.Id,
            datos => _notificaciones.CitaAceptada(datos, cita.LinkReunion, request.Notas),
            cancellationToken);

        return Ok(new { Mensaje = "Cita aceptada" });
    }

    [HttpPut("{id}/rechazar")]
    [Authorize(Policy = PoliticasAutorizacion.TutorAprobado)]
    public async Task<IActionResult> RechazarCita(
        int id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RechazarCitaRequestDto? request = null,
        CancellationToken cancellationToken = default)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int tutorId))
        {
            return Problem(statusCode: 401, title: "Token inválido.");
        }

        var cita = await _context.Citas.FindAsync([id], cancellationToken);
        if (cita == null)
        {
            return Problem(statusCode: 404, title: "La cita no existe.");
        }

        if (cita.TutorId != tutorId)
        {
            return Forbid();
        }

        // Una cita ya aceptada se cancela (avisa con motivo), no se rechaza.
        if (cita.Estado != "Pendiente")
        {
            return Problem(statusCode: 409, title: "Solo se pueden rechazar solicitudes pendientes.");
        }

        var motivo = request?.Motivo?.Trim();
        if (!string.IsNullOrEmpty(motivo))
        {
            cita.Notas = string.IsNullOrWhiteSpace(cita.Notas)
                ? $"Motivo de rechazo: {motivo}"
                : $"{cita.Notas}{Environment.NewLine}Motivo de rechazo: {motivo}";
        }

        cita.Estado = "Rechazada";

        await _context.SaveChangesAsync(cancellationToken);

        await NotificarAsync(cita.Id, datos => _notificaciones.CitaRechazada(datos, motivo), cancellationToken);

        return Ok(new { Mensaje = "Cita rechazada" });
    }

    [HttpGet("pendientes")]
    [Authorize(Policy = PoliticasAutorizacion.TutorAprobado)]
    public async Task<ActionResult<IReadOnlyList<CitaPendienteResponseDto>>> ObtenerPendientes(
        CancellationToken cancellationToken = default)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userIdClaim) ||
            !int.TryParse(userIdClaim, out int tutorId))
        {
            return Problem(statusCode: 401, title: "Token inválido.");
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
            return Problem(statusCode: 401, title: "Usuario no autorizado o token inválido.");
        }

        var ahora = _hora.Ahora;

        IQueryable<ProximaCitaResponseDto> query;

        if (userRole == "Estudiante")
        {
            query = _context.Citas
                .AsNoTracking()
                .Where(c => c.EstudianteId == userId &&
                            (c.Estado == "Aceptada" || c.Estado == "Pendiente") &&
                            c.FechaHoraInicio >= ahora)
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
                            c.FechaHoraInicio >= ahora)
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
            return Problem(statusCode: 401, title: "Token inválido.");
        }

        var cita = await _context.Citas.FindAsync([id], cancellationToken);
        if (cita == null)
        {
            return Problem(statusCode: 404, title: "La cita no existe.");
        }

        if (cita.TutorId != userId && cita.EstudianteId != userId)
        {
            return Forbid();
        }

        if (cita.Estado != "Pendiente" && cita.Estado != "Aceptada")
        {
            return Problem(statusCode: 400, title: "Solo se pueden cancelar citas pendientes o aceptadas");
        }

        if (!string.IsNullOrWhiteSpace(request.Motivo))
        {
            cita.Notas = string.IsNullOrWhiteSpace(cita.Notas)
                ? $"Motivo de cancelación: {request.Motivo.Trim()}"
                : $"{cita.Notas}{Environment.NewLine}Motivo de cancelación: {request.Motivo.Trim()}";
        }

        cita.Estado = "Cancelada";
        await _context.SaveChangesAsync(cancellationToken);

        var canceladaPorTutor = cita.TutorId == userId;
        await NotificarAsync(
            cita.Id,
            datos => _notificaciones.CitaCancelada(datos, canceladaPorTutor, request.Motivo),
            cancellationToken);

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
            return Problem(statusCode: 401, title: "Token inválido.");
        }

        var ahora = _hora.Ahora;
        var citasQuery = _context.Citas
            .Where(cita =>
                (userRole == "Estudiante" && cita.EstudianteId == userId) ||
                (userRole == "Tutor" && cita.TutorId == userId));

        var citasCompletadas = await citasQuery
            .Where(cita => cita.Estado == "Aceptada" && cita.FechaHoraInicio < ahora)
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
                cita.FechaHoraInicio < ahora ||
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
                Estado = cita.Estado,
                Puntuacion = cita.Resena == null ? null : cita.Resena.Puntuacion
            })
            .ToListAsync(cancellationToken);

        return userRole == "Estudiante" || userRole == "Tutor"
            ? Ok(historial)
            : Forbid();
    }

    [HttpPut("{id}/completar")]
    [Authorize(Policy = PoliticasAutorizacion.TutorAprobado)]
    public async Task<IActionResult> CompletarCita(int id, CancellationToken cancellationToken = default)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                       ?? User.FindFirst("sub")?.Value
                       ?? User.FindFirst("id")?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var tutorId))
        {
            return Problem(statusCode: 401, title: "Token inválido.");
        }

        var cita = await _context.Citas.FindAsync([id], cancellationToken);
        if (cita == null)
        {
            return Problem(statusCode: 404, title: "La cita no existe.");
        }

        if (cita.TutorId != tutorId)
        {
            return Forbid();
        }

        if (cita.Estado != "Aceptada")
        {
            return Problem(statusCode: 400, title: "Solo se pueden completar citas aceptadas");
        }

        cita.Estado = "Completada";
        await _context.SaveChangesAsync(cancellationToken);
        return Ok(new { Mensaje = "Cita completada" });
    }

    private static bool EsEnlaceWeb(string enlace) =>
        enlace.Length <= 255 &&
        Uri.TryCreate(enlace, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
}
