using backend.Data;
using backend.DTOs.Responses;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace backend.Controllers;

[ApiController]
[Route("api/materias")]
public class MateriasController : ControllerBase
{
    private readonly AppDbContext _context;

    public MateriasController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MateriaResponseDto>>> ObtenerMaterias(
        CancellationToken cancellationToken)
    {
        var materias = await _context.Materias
            .AsNoTracking()
            .OrderBy(materia => materia.Id)
            .Select(materia => new MateriaResponseDto
            {
                Id = materia.Id,
                Nombre = materia.Nombre,
                CategoriaId = materia.CategoriaId,
                Categoria = materia.Categoria.Nombre
            })
            .ToListAsync(cancellationToken);

        return Ok(materias);
    }
}