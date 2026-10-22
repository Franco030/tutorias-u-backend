using System.ComponentModel.DataAnnotations;

namespace backend.DTOs.Requests;

// Motivo opcional del rechazo de un aspirante a tutor; se incluye en el correo.
public sealed record RechazarTutorRequest(
    [property: MaxLength(500, ErrorMessage = "El motivo no puede superar los 500 caracteres.")]
    string? Motivo);
