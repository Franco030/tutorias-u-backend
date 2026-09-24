using backend.Data;
using backend.DTOs.Requests;
using backend.Enums;
using backend.Models;
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
        public async Task<IActionResult> Aplicar([FromBody] AplicarTutorRequestDto request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("id");

            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int usuarioId))
            {
                return Unauthorized(new { mensaje = "No se pudo identificar al usuario autenticado en el token." });
            }

            var usuario = await _context.Usuarios.FindAsync(usuarioId);
            if (usuario == null)
            {
                return NotFound(new { mensaje = "El usuario no existe." });
            }

            if (usuario.EstadoAprobacionId == (int)EstadoAprobacion.Pendiente || usuario.EstadoAprobacionId == (int)EstadoAprobacion.Aprobado)
            {
                return BadRequest(new { mensaje = "El usuario ya tiene una solicitud en proceso o ya es un tutor." });
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
                    Materias = tutor.MateriaNavigation.Select(m => m.Nombre).ToList()
                })
                .ToListAsync();

            return Ok(tutores);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetTutorProfile(int id)
        {
            var tutor = await _context.Usuarios
                .Include(u => u.MateriaNavigation)
                .FirstOrDefaultAsync(u => u.Id == id && u.Rol == "Tutor" && u.EstadoAprobacionId == (int)EstadoAprobacion.Aprobado);

            if (tutor == null)
            {
                return NotFound(new { mensaje = "Tutor no encontrado o no está aprobado." });
            }

            var response = new backend.DTOs.TutorPerfilResponseDto
            {
                Id = tutor.Id,
                Nombre = tutor.Nombre,
                FotoUrl = tutor.FotoUrl,
                CalificacionPromedio = tutor.CalificacionPromedio,
                Materias = tutor.MateriaNavigation.Select(m => m.Nombre).ToList()
            };

            return Ok(response);
        }

        [HttpPut("mis-materias")]
        [Authorize(Roles = "Tutor")]
        public async Task<IActionResult> ActualizarMisMaterias([FromBody] ActualizarMateriasTutorDto request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("id");
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int usuarioId))
            {
                return Unauthorized(new { mensaje = "No se pudo identificar al usuario autenticado." });
            }

            var tutor = await _context.Usuarios
                .Include(u => u.MateriaNavigation)
                .FirstOrDefaultAsync(u => u.Id == usuarioId);

            if (tutor == null)
            {
                return NotFound(new { mensaje = "Usuario no encontrado." });
            }

            if (tutor.EstadoAprobacionId != (int)EstadoAprobacion.Aprobado)
            {
                return BadRequest(new { mensaje = "El usuario no es un tutor aprobado." });
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
    }
}
