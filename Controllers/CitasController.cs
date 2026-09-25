using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using backend.Data;
using backend.Models;
using backend.DTOs.Citas;
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

        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int estudianteId))
        {
            return Unauthorized();
        }

        var cita = new Cita
        {
            EstudianteId = estudianteId,
            TutorId = request.TutorId,
            MateriaId = request.MateriaId,
            FechaHoraInicio = request.FechaHoraInicio,
            Estado = "Aceptada"
        };

        _context.Citas.Add(cita);
        await _context.SaveChangesAsync();

        return Created("", new { Mensaje = "Cita agendada exitosamente", CitaId = cita.Id });
        
    }
}
