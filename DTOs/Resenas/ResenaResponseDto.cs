namespace backend.DTOs.Resenas;

public sealed class ResenaResponseDto
{
    public int Id { get; init; }

    public int Puntuacion { get; init; }

    public string? Comentario { get; init; }

    public DateTime FechaCreacion { get; init; }

    public string EstudianteNombre { get; init; } = null!;

    public string? EstudianteFotoUrl { get; init; }
}
