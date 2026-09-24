namespace backend.DTOs
{
    public class TutorPerfilResponseDto
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = null!;
        public string? FotoUrl { get; set; }
        public decimal? CalificacionPromedio { get; set; }
        public List<string> Materias { get; set; } = new List<string>();
    }
}
