using System.ComponentModel.DataAnnotations;

namespace backend.DTOs.Requests;

// Preferencias de reserva del tutor.
public sealed record PreferenciasTutorDto
{
    // Horas mínimas de anticipación para que un estudiante reserve (0 = sin mínimo).
    [Range(0, 168, ErrorMessage = "La antelación debe estar entre 0 y 168 horas.")]
    public int AntelacionMinimaHoras { get; init; }

    // Enlace de videollamada que se usa por defecto al aceptar una cita.
    [MaxLength(255, ErrorMessage = "El enlace no puede superar los 255 caracteres.")]
    public string? LinkReunion { get; init; }
}
