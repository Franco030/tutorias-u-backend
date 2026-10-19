using System.ComponentModel.DataAnnotations;

namespace backend.DTOs.Citas;

public sealed record CancelarCitaRequestDto
{
    [MaxLength(1000)]
    public string? Motivo { get; init; }
}
