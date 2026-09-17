using System.ComponentModel.DataAnnotations;

namespace backend.DTOs.Requests;

public class EstudianteInteresesRequestDto
{
    [Required]
    [MinLength(1, ErrorMessage = "Debes seleccionar al menos una materia.")]
    public List<int> MateriaIds { get; set; } = new();
}
