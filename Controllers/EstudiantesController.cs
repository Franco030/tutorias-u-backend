using System.Security.Claims;
using backend.Data;
using backend.DTOs.Requests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace backend.Controllers;

[ApiController]
[Route("api/estudiantes")]
[Authorize]
public class EstudiantesController : ControllerBase
{
    private readonly AppDbContext _context;

    public EstudiantesController(AppDbContext context)
    {
        _context = context;
    }

    [HttpPost("intereses")]
    public async Task<IActionResult> GuardarIntereses(
        [FromBody] EstudianteInteresesRequestDto dto,
        CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (userIdClaim == null || !int.TryParse(userIdClaim, out int estudianteId))
        {
            return Unauthorized(new { message = "Token inválido." });
        }

        var estudiante = await _context.Usuarios
            .Include(usuario => usuario.Materia)
            .FirstOrDefaultAsync(
                usuario => usuario.Id == estudianteId,
                cancellationToken);

        if (estudiante == null)
        {
            return NotFound(new { message = "Usuario no encontrado." });
        }

        if (estudiante.Rol != "Estudiante")
        {
            return BadRequest(new
            {
                message = "El usuario autenticado no tiene rol de Estudiante."
            });
        }

        var materiaIds = dto.MateriaIds
            .Distinct()
            .ToList();

        if (materiaIds.Count == 0)
        {
            return BadRequest(new
            {
                message = "Debes seleccionar al menos una materia."
            });
        }

        var materias = await _context.Materias
            .Where(materia => materiaIds.Contains(materia.Id))
            .ToListAsync(cancellationToken);

        if (materias.Count != materiaIds.Count)
        {
            return BadRequest(new
            {
                message = "Una o más materias seleccionadas no existen."
            });
        }

        estudiante.Materia.Clear();

        foreach (var materia in materias)
        {
            estudiante.Materia.Add(materia);
        }

        await _context.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            message = "Intereses guardados correctamente.",
            materiaIds
        });
    }
}
