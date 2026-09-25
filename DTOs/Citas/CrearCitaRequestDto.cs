using System;
using System.ComponentModel.DataAnnotations;

namespace backend.DTOs.Citas;

public class CrearCitaRequestDto
{
    [Required]
    public int TutorId { get; set; }

    [Required]
    public int MateriaId { get; set; }

    [Required]
    public DateTime FechaHoraInicio { get; set; }
}
