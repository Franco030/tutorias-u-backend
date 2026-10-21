using System.ComponentModel.DataAnnotations;

namespace backend.DTOs.Resenas;

public sealed record CrearResenaRequestDto
{
    [Required]
    public int CitaId { get; init; }

    [Range(1, 5)]
    public int Puntuacion { get; init; }

    [MaxLength(1000)]
    public string? Comentario { get; init; }
}
