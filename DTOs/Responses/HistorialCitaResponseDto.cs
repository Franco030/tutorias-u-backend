namespace backend.DTOs.Responses;

public class HistorialCitaResponseDto
{
    public int Id { get; set; }

    public string RolOpuestoNombre { get; set; } = string.Empty;

    public string Materia { get; set; } = string.Empty;

    public DateTime FechaHoraInicio { get; set; }

    public string Estado { get; set; } = string.Empty;

    public string? LinkReunion { get; set; }

    public string? Notas { get; set; }
}