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
        public string Estado { get; set; } = string.Empty;
        // Puntuación que el estudiante dio a la sesión; null si aún no la califica.
        public int? Puntuacion { get; set; }
    }
}