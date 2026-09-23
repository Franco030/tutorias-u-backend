using backend.Data;
using backend.DTOs.Requests;
using backend.DTOs.Responses;
using backend.Enums;
using backend.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

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

        [HttpGet("{id:int}")]
        public async Task<ActionResult<TutorPerfilResponseDto>> ObtenerPerfil(int id)
        {
            var tutor = await _context.Usuarios
                .AsNoTracking()
                .Include(usuario => usuario.TutorMaterias)
                    .ThenInclude(tutorMateria => tutorMateria.Materia)
                .FirstOrDefaultAsync(usuario =>
                    usuario.Id == id &&
                    usuario.Rol == "Tutor" &&
                    usuario.EstadoAprobacionId == (int)EstadoAprobacion.Aprobado);

            if (tutor is null)
            {
                return NotFound();
            }

            return Ok(new TutorPerfilResponseDto
            {
                Id = tutor.Id,
                Nombre = tutor.Nombre,
                FotoUrl = tutor.FotoUrl,
                Materias = tutor.TutorMaterias
                    .Select(tutorMateria => new MateriaResponseDto
                    {
                        Id = tutorMateria.Materia.Id,
                        Nombre = tutorMateria.Materia.Nombre,
                        CategoriaId = tutorMateria.Materia.CategoriaId
                    })
                    .ToList()
            });
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
    }
}
