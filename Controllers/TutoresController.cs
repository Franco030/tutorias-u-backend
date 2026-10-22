using backend.Authorization;
using backend.Data;
using backend.DTOs.Requests;
using backend.DTOs.Responses;
using backend.Enums;
using backend.Models;
using backend.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

namespace backend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TutoresController : ControllerBase
    {
        private readonly AppDbContext _context;

        public TutoresController(AppDbContext context)
        {
            _context = context;
        }

        [HttpPost("aplicar")]
        [Authorize]
        public async Task<IActionResult> Aplicar(
            [FromBody] AplicarTutorRequestDto request,
            [FromServices] IBlobService blobService)
        {
            // Solo un comprobante subido a nuestro almacenamiento: así el
            // administrador nunca abre un enlace arbitrario del aspirante.
            if (!blobService.EsComprobantePropio(request.UrlCredencial))
            {
                return Problem(statusCode: 400, title: "Sube tu comprobante desde la aplicación.");
            }

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("id");

            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int usuarioId))
            {
                return Problem(statusCode: 401, title: "No se pudo identificar al usuario autenticado en el token.");
            }

            var usuario = await _context.Usuarios.FindAsync(usuarioId);
            if (usuario == null)
            {
                return Problem(statusCode: 404, title: "El usuario no existe.");
            }

            if (usuario.EstadoAprobacionId == (int)EstadoAprobacion.Pendiente || usuario.EstadoAprobacionId == (int)EstadoAprobacion.Aprobado)
            {
                return Problem(statusCode: 400, title: "El usuario ya tiene una solicitud en proceso o ya es un tutor.");
            }

            var nuevaSolicitud = new TutorSolicitudesCredenciale
            {
                UsuarioId = usuarioId,
                UrlCredencial = request.UrlCredencial
            };

            foreach (var materiaId in request.MateriaIds)
            {
                nuevaSolicitud.SolicitudMateria.Add(new SolicitudMateria
                {
                    MateriaId = materiaId
                });
            }

            _context.TutorSolicitudesCredenciales.Add(nuevaSolicitud);

            usuario.EstadoAprobacionId = (int)EstadoAprobacion.Pendiente;

            await _context.SaveChangesAsync();

            return Ok(new { mensaje = "Solicitud de tutoria enviada con exito. Su perfil esta ahora pendiente de revision." });
        }

        [HttpGet]
        public async Task<IActionResult> GetAllTutors()
        {
            var tutores = await _context.Usuarios
                .Include(u => u.MateriaNavigation)
                .Where(u => u.Rol == "Tutor" && u.EstadoAprobacionId == (int)EstadoAprobacion.Aprobado)
                .Select(tutor => new backend.DTOs.TutorPerfilResponseDto
                {
                    Id = tutor.Id,
                    Nombre = tutor.Nombre,
                    FotoUrl = tutor.FotoUrl,
                    CalificacionPromedio = tutor.CalificacionPromedio,
                    TotalResenas = tutor.ResenaTutors.Count(),
                    Materias = tutor.MateriaNavigation.Select(m => m.Nombre).ToList()
                })
                .ToListAsync();

            return Ok(tutores);
        }

        [HttpGet("recomendados")]
        [Authorize]
        public async Task<IActionResult> GetRecommendedTutors(CancellationToken cancellationToken)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("id");

            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int estudianteId))
            {
                return Problem(statusCode: 401, title: "No se pudo identificar al estudiante autenticado en el token.");
            }

            var estudiante = await _context.Usuarios
                .Where(usuario => usuario.Id == estudianteId)
                .Select(usuario => new
                {
                    usuario.Rol,
                    MateriaIds = usuario.Materia.Select(materia => materia.Id).ToList()
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (estudiante == null)
            {
                return Problem(statusCode: 404, title: "El estudiante no existe.");
            }

            if (estudiante.Rol != "Estudiante")
            {
                return Problem(statusCode: 400, title: "El usuario autenticado no tiene rol de Estudiante.");
            }

            if (estudiante.MateriaIds.Count == 0)
            {
                return Ok(new List<backend.DTOs.TutorPerfilResponseDto>());
            }

            var tutoresRecomendados = await _context.Usuarios
                .Where(usuario =>
                    usuario.Rol == "Tutor" &&
                    usuario.EstadoAprobacionId == (int)EstadoAprobacion.Aprobado &&
                    usuario.MateriaNavigation.Any(materia =>
                        estudiante.MateriaIds.Contains(materia.Id)))
                .OrderByDescending(usuario => usuario.CalificacionPromedio)
                .Take(10)
                .Select(tutor => new backend.DTOs.TutorPerfilResponseDto
                {
                    Id = tutor.Id,
                    Nombre = tutor.Nombre,
                    FotoUrl = tutor.FotoUrl,
                    CalificacionPromedio = tutor.CalificacionPromedio,
                    TotalResenas = tutor.ResenaTutors.Count(),
                    Materias = tutor.MateriaNavigation
                        .Select(materia => materia.Nombre)
                        .ToList()
                })
                .ToListAsync(cancellationToken);

            return Ok(tutoresRecomendados);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetTutorProfile(int id)
        {
            var tutor = await _context.Usuarios
                .Include(u => u.MateriaNavigation)
                .FirstOrDefaultAsync(u => u.Id == id && u.Rol == "Tutor" && u.EstadoAprobacionId == (int)EstadoAprobacion.Aprobado);

            if (tutor == null)
            {
                return Problem(statusCode: 404, title: "Tutor no encontrado o no está aprobado.");
            }

            var response = new backend.DTOs.TutorPerfilResponseDto
            {
                Id = tutor.Id,
                Nombre = tutor.Nombre,
                FotoUrl = tutor.FotoUrl,
                CalificacionPromedio = tutor.CalificacionPromedio,
                TotalResenas = await _context.Resenas.CountAsync(resena => resena.TutorId == tutor.Id),
                AntelacionMinimaHoras = tutor.AntelacionMinimaHoras,
                Materias = tutor.MateriaNavigation.Select(m => m.Nombre).ToList()
            };

            return Ok(response);
        }

        [HttpPut("mis-materias")]
        [Authorize(Policy = PoliticasAutorizacion.TutorAprobado)]
        public async Task<IActionResult> ActualizarMisMaterias([FromBody] ActualizarMateriasTutorDto request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("id");
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int usuarioId))
            {
                return Problem(statusCode: 401, title: "No se pudo identificar al usuario autenticado.");
            }

            var tutor = await _context.Usuarios
                .Include(u => u.MateriaNavigation)
                .FirstOrDefaultAsync(u => u.Id == usuarioId);

            if (tutor == null)
            {
                return Problem(statusCode: 404, title: "Usuario no encontrado.");
            }

            if (tutor.EstadoAprobacionId != (int)EstadoAprobacion.Aprobado)
            {
                return Problem(statusCode: 400, title: "El usuario no es un tutor aprobado.");
            }

            // Limpiar las materias actuales
            tutor.MateriaNavigation.Clear();

            // Buscar y asignar las nuevas materias
            var nuevasMaterias = await _context.Materias
                .Where(m => request.MateriaIds.Contains(m.Id))
                .ToListAsync();

            foreach (var mat in nuevasMaterias)
            {
                tutor.MateriaNavigation.Add(mat);
            }

            await _context.SaveChangesAsync();

            return Ok(new { mensaje = "Materias actualizadas con éxito." });
        }

        [HttpPost("disponibilidad")]
        [Authorize(Policy = PoliticasAutorizacion.TutorAprobado)]
        public async Task<IActionResult> GuardarDisponibilidad([FromBody] List<DisponibilidadRequestDto> request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("id");
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int tutorId))
            {
                return Problem(statusCode: 401, title: "No se pudo identificar al tutor autenticado.");
            }

            var tutorExiste = await _context.Usuarios
                .AnyAsync(usuario => usuario.Id == tutorId && usuario.Rol == "Tutor");
            if (!tutorExiste)
            {
                return Problem(statusCode: 404, title: "Tutor no encontrado.");
            }

            for (var i = 0; i < request.Count; i++)
            {
                var bloque = request[i];
                if (bloque.DiaSemana > 6)
                {
                    return Problem(statusCode: 400, title: "El día de la semana debe estar entre 0 y 6.");
                }

                if (bloque.HoraFin <= bloque.HoraInicio)
                {
                    return Problem(statusCode: 400, title: "La hora de fin debe ser posterior a la hora de inicio.");
                }

                for (var j = i + 1; j < request.Count; j++)
                {
                    var otroBloque = request[j];
                    if (bloque.DiaSemana == otroBloque.DiaSemana &&
                        bloque.HoraInicio < otroBloque.HoraFin &&
                        bloque.HoraFin > otroBloque.HoraInicio)
                    {
                        return Problem(statusCode: 400, title: "El horario se cruza con otro bloque de disponibilidad.");
                    }
                }
            }

            var disponibilidadActual = await _context.TutorDisponibilidads
                .Where(disponibilidad => disponibilidad.TutorId == tutorId)
                .ToListAsync();
            _context.TutorDisponibilidads.RemoveRange(disponibilidadActual);
            var disponibilidadNueva = request.Select(bloque => new TutorDisponibilidad
            {
                TutorId = tutorId,
                DiaSemana = bloque.DiaSemana,
                HoraInicio = bloque.HoraInicio,
                HoraFin = bloque.HoraFin
            }).ToList();
            _context.TutorDisponibilidads.AddRange(disponibilidadNueva);
            await _context.SaveChangesAsync();

            return Ok(disponibilidadNueva
                .OrderBy(bloque => bloque.DiaSemana)
                .ThenBy(bloque => bloque.HoraInicio)
                .Select(bloque => new DisponibilidadResponseDto
                {
                    Id = bloque.Id,
                    DiaSemana = bloque.DiaSemana,
                    HoraInicio = bloque.HoraInicio,
                    HoraFin = bloque.HoraFin
                })
                .ToList());
        }

        [HttpGet("{id}/disponibilidad")]
        public async Task<IActionResult> GetDisponibilidad(int id)
        {
            var tutorExiste = await _context.Usuarios
                .AnyAsync(usuario => usuario.Id == id && usuario.Rol == "Tutor");
            if (!tutorExiste)
            {
                return Problem(statusCode: 404, title: "Tutor no encontrado.");
            }

            var disponibilidades = await _context.TutorDisponibilidads
                .Where(disponibilidad => disponibilidad.TutorId == id)
                .OrderBy(disponibilidad => disponibilidad.DiaSemana)
                .ThenBy(disponibilidad => disponibilidad.HoraInicio)
                .Select(disponibilidad => new DisponibilidadResponseDto
                {
                    Id = disponibilidad.Id,
                    DiaSemana = disponibilidad.DiaSemana,
                    HoraInicio = disponibilidad.HoraInicio,
                    HoraFin = disponibilidad.HoraFin
                })
                .ToListAsync();

            return Ok(disponibilidades);
        }

        // Preferencias de reserva del tutor autenticado.
        [HttpGet("preferencias")]
        [Authorize(Policy = PoliticasAutorizacion.TutorAprobado)]
        public async Task<ActionResult<PreferenciasTutorDto>> ObtenerPreferencias(CancellationToken cancellationToken)
        {
            var tutor = await TutorAutenticadoAsync(cancellationToken);
            if (tutor == null) return Problem(statusCode: 401, title: "Token inválido.");

            return Ok(new PreferenciasTutorDto
            {
                AntelacionMinimaHoras = tutor.AntelacionMinimaHoras,
                LinkReunion = tutor.LinkReunion
            });
        }

        [HttpPut("preferencias")]
        [Authorize(Policy = PoliticasAutorizacion.TutorAprobado)]
        public async Task<ActionResult<PreferenciasTutorDto>> GuardarPreferencias(
            [FromBody] PreferenciasTutorDto request,
            CancellationToken cancellationToken)
        {
            var tutor = await TutorAutenticadoAsync(cancellationToken);
            if (tutor == null) return Problem(statusCode: 401, title: "Token inválido.");

            if (request.AntelacionMinimaHoras is < 0 or > 168)
            {
                return Problem(statusCode: 400, title: "La antelación debe estar entre 0 y 168 horas.");
            }

            var link = request.LinkReunion?.Trim();
            if (!string.IsNullOrEmpty(link) && !EsEnlaceWeb(link))
            {
                return Problem(statusCode: 400, title: "El enlace de reunión debe ser una dirección web (https://…).");
            }

            tutor.AntelacionMinimaHoras = request.AntelacionMinimaHoras;
            tutor.LinkReunion = string.IsNullOrEmpty(link) ? null : link;
            await _context.SaveChangesAsync(cancellationToken);

            return Ok(new PreferenciasTutorDto
            {
                AntelacionMinimaHoras = tutor.AntelacionMinimaHoras,
                LinkReunion = tutor.LinkReunion
            });
        }

        // Días bloqueados (desde hoy) del tutor autenticado, con su motivo.
        [HttpGet("bloqueos")]
        [Authorize(Policy = PoliticasAutorizacion.TutorAprobado)]
        public async Task<ActionResult<BloqueosResponseDto>> ObtenerMisBloqueos(
            [FromServices] IHoraPlataforma hora,
            CancellationToken cancellationToken)
        {
            var tutor = await TutorAutenticadoAsync(cancellationToken);
            if (tutor == null) return Problem(statusCode: 401, title: "Token inválido.");

            return Ok(await RespuestaBloqueosAsync(tutor.Id, DateOnly.FromDateTime(hora.Ahora), cancellationToken));
        }

        /*
         * Reemplaza los días bloqueados desde hoy en adelante (los pasados no se
         * tocan). No cancela citas: informa cuántas caen en días bloqueados.
         */
        [HttpPut("bloqueos")]
        [Authorize(Policy = PoliticasAutorizacion.TutorAprobado)]
        public async Task<ActionResult<BloqueosResponseDto>> GuardarBloqueos(
            [FromBody] List<BloqueoDto> request,
            [FromServices] IHoraPlataforma hora,
            CancellationToken cancellationToken)
        {
            var tutor = await TutorAutenticadoAsync(cancellationToken);
            if (tutor == null) return Problem(statusCode: 401, title: "Token inválido.");

            var hoy = DateOnly.FromDateTime(hora.Ahora);
            var limite = hoy.AddDays(MaximoDiasBloqueo);
            if (request.Any(bloqueo => bloqueo.Fecha < hoy || bloqueo.Fecha > limite))
            {
                return Problem(statusCode: 400, title: $"Solo se pueden bloquear días entre hoy y los próximos {MaximoDiasBloqueo} días.");
            }

            if (request.Any(bloqueo => bloqueo.Motivo?.Trim().Length > 200))
            {
                return Problem(statusCode: 400, title: "El motivo no puede superar los 200 caracteres.");
            }

            var nuevos = request
                .GroupBy(bloqueo => bloqueo.Fecha)
                .Select(grupo => new Bloqueo
                {
                    TutorId = tutor.Id,
                    Fecha = grupo.Key,
                    Motivo = string.IsNullOrWhiteSpace(grupo.First().Motivo) ? null : grupo.First().Motivo!.Trim()
                })
                .ToList();

            var actuales = await _context.Bloqueos
                .Where(bloqueo => bloqueo.TutorId == tutor.Id && bloqueo.Fecha >= hoy)
                .ToListAsync(cancellationToken);
            _context.Bloqueos.RemoveRange(actuales);
            _context.Bloqueos.AddRange(nuevos);
            await _context.SaveChangesAsync(cancellationToken);

            return Ok(await RespuestaBloqueosAsync(tutor.Id, hoy, cancellationToken));
        }

        // Días bloqueados de un tutor para el calendario de reservas (sin el motivo).
        [HttpGet("{id}/bloqueos")]
        [Authorize]
        public async Task<ActionResult<IReadOnlyList<DateOnly>>> ObtenerBloqueosTutor(
            int id,
            [FromServices] IHoraPlataforma hora,
            CancellationToken cancellationToken)
        {
            var hoy = DateOnly.FromDateTime(hora.Ahora);
            var fechas = await _context.Bloqueos
                .Where(bloqueo => bloqueo.TutorId == id && bloqueo.Fecha >= hoy)
                .OrderBy(bloqueo => bloqueo.Fecha)
                .Select(bloqueo => bloqueo.Fecha)
                .ToListAsync(cancellationToken);

            return Ok(fechas);
        }

        private const int MaximoDiasBloqueo = 366;

        private async Task<Usuario?> TutorAutenticadoAsync(CancellationToken cancellationToken)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("id");
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int tutorId))
            {
                return null;
            }

            return await _context.Usuarios.FirstOrDefaultAsync(usuario => usuario.Id == tutorId, cancellationToken);
        }

        private async Task<BloqueosResponseDto> RespuestaBloqueosAsync(int tutorId, DateOnly hoy, CancellationToken cancellationToken)
        {
            var bloqueos = await _context.Bloqueos
                .Where(bloqueo => bloqueo.TutorId == tutorId && bloqueo.Fecha >= hoy)
                .OrderBy(bloqueo => bloqueo.Fecha)
                .Select(bloqueo => new BloqueoDto { Fecha = bloqueo.Fecha, Motivo = bloqueo.Motivo })
                .ToListAsync(cancellationToken);

            var fechas = bloqueos.Select(bloqueo => bloqueo.Fecha.ToDateTime(TimeOnly.MinValue)).ToList();
            var citasAfectadas = fechas.Count == 0
                ? 0
                : await _context.Citas.CountAsync(cita =>
                    cita.TutorId == tutorId &&
                    (cita.Estado == "Pendiente" || cita.Estado == "Aceptada") &&
                    fechas.Contains(cita.FechaHoraInicio.Date),
                    cancellationToken);

            return new BloqueosResponseDto { Bloqueos = bloqueos, CitasAfectadas = citasAfectadas };
        }

        private static bool EsEnlaceWeb(string enlace) =>
            enlace.Length <= 255 &&
            Uri.TryCreate(enlace, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
    }
}
