namespace backend.DTOs.Citas;

public class CitaPendienteResponseDto
{
    public int Id { get; set; }

    public int EstudianteId { get; set; }

    public string EstudianteNombre { get; set; } = string.Empty;

    public int MateriaId { get; set; }

    public string MateriaNombre { get; set; } = string.Empty;

    public DateTime FechaHoraInicio { get; set; }

    public string Estado { get; set; } = string.Empty;
}