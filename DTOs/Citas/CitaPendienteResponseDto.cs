namespace backend.DTOs.Citas;

public class CitaPendienteResponseDto
{
    public int CitaId { get; set; }

    public string AlumnoNombre { get; set; } = string.Empty;

    public string MateriaNombre { get; set; } = string.Empty;

    public DateTime FechaHoraInicio { get; set; }
}