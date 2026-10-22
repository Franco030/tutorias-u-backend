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

    [Required]
    public DateTime FechaHoraFin { get; set; }

    // Qué quiere repasar el estudiante (opcional).
    [MaxLength(500, ErrorMessage = "El tema no puede superar los 500 caracteres.")]
    public string? Tema { get; set; }
}
