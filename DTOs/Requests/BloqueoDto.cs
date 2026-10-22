using System.ComponentModel.DataAnnotations;

namespace backend.DTOs.Requests;

// Día en que el tutor no da tutorías.
public sealed record BloqueoDto
{
    public DateOnly Fecha { get; init; }

    [MaxLength(200, ErrorMessage = "El motivo no puede superar los 200 caracteres.")]
    public string? Motivo { get; init; }
}
