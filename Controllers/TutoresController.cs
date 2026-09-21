using System.Security.Claims;
using backend.DTOs.Requests;
using backend.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace backend.Controllers
{
    [ApiController]
    [Route("api/tutores")]
    public class TutoresController : ControllerBase
    {
        private readonly TutoriasUDevContext _context;

        public TutoresController(TutoriasUDevContext context)
        {
            _context = context;
        }

        [HttpPost("aplicar")]
        [Authorize]
        public async Task<IActionResult> Aplicar([FromBody] AplicarTutorRequestDto dto)
        {
            // Validar ModelState (Atributos [Required], [Url], [MinLength])
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            // Validación explícita de lista vacía
            if (dto.MateriaIds == null || !dto.MateriaIds.Any())
            {
                return BadRequest(new { mensaje = "Debe seleccionar al menos una materia para postularse." });
            }

            // Extraer el ID del usuario desde las Claims del JWT
            var usuarioIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                                 ?? User.FindFirst("sub")?.Value
                                 ?? User.FindFirst("UsuarioId")?.Value;

            if (string.IsNullOrEmpty(usuarioIdClaim) || !int.TryParse(usuarioIdClaim, out int usuarioId))
            {
                return Unauthorized(new { mensaje = "Token no válido o sin identificación de usuario." });
            }

            // 1. Obtener o registrar estado 'Pendiente'
            var estadoPendiente = await _context.EstadosAprobacions
                .FirstOrDefaultAsync(e => e.Estado == "Pendiente");

            if (estadoPendiente == null)
            {
                estadoPendiente = new EstadosAprobacion { Estado = "Pendiente" };
                _context.EstadosAprobacions.Add(estadoPendiente);
                await _context.SaveChangesAsync();
            }

            // 2. Actualizar el estado del Usuario
            var usuario = await _context.Usuarios.FindAsync(usuarioId);
            if (usuario == null)
            {
                return NotFound(new { mensaje = "Usuario no encontrado." });
            }

            usuario.EstadoAprobacionId = estadoPendiente.Id;

            // 3. Registrar la solicitud
            var solicitud = new TutorSolicitudesCredenciale
            {
                UsuarioId = usuarioId,
                UrlCredencial = dto.UrlCredencial,
                FechaSolicitud = DateTime.UtcNow
            };

            _context.TutorSolicitudesCredenciales.Add(solicitud);
            await _context.SaveChangesAsync();

            // 4. Guardar materias asociadas
            foreach (var materiaId in dto.MateriaIds)
            {
                var relacion = new SolicitudMateria
                {
                    SolicitudId = solicitud.Id,
                    MateriaId = materiaId
                };
                _context.SolicitudMaterias.Add(relacion);
            }
            await _context.SaveChangesAsync();

            return Ok(new { mensaje = "Tu solicitud está en revisión" });
        }
    }
}