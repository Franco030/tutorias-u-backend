namespace backend.DTOs.Responses
{
    public class ProximaCitaResponseDto
    {
        public int Id { get; set; }
        public string RolOpuestoNombre { get; set; } = string.Empty;
        public string Materia { get; set; } = string.Empty;
        public DateTime FechaHoraInicio { get; set; }
        public string? LinkReunion { get; set; }
        public string? Notas { get; set; }
    }
}