using System.ComponentModel.DataAnnotations;

namespace backend.DTOs.Requests;

public class DisponibilidadRequestDto
{
    [Range(0, 6)]
    public byte DiaSemana { get; set; }

    [Required]
    public TimeOnly HoraInicio { get; set; }

    [Required]
    public TimeOnly HoraFin { get; set; }
}
