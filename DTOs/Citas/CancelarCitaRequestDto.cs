using System.ComponentModel.DataAnnotations;

namespace backend.DTOs.Citas;

public class CancelarCitaRequestDto
{
    [StringLength(1000)]
    public string? Motivo { get; set; }
}