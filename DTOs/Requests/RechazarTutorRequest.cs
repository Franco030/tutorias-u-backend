using System.ComponentModel.DataAnnotations;

namespace backend.DTOs.Requests;

// Motivo opcional del rechazo de un aspirante a tutor; se incluye en el correo.
// En un record posicional la validación va en el parámetro, no con
// "property:": así ASP.NET la lanzaba como error 500 al leer el cuerpo.
public sealed record RechazarTutorRequest(
    [MaxLength(500, ErrorMessage = "El motivo no puede superar los 500 caracteres.")]
    string? Motivo);
