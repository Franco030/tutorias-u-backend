namespace backend.DTOs.Responses;

public sealed class TutorPerfilResponseDto
{
    public int Id { get; init; }

    public string Nombre { get; init; } = string.Empty;

    public string? FotoUrl { get; init; }

    public IReadOnlyList<MateriaResponseDto> Materias { get; init; } = [];
}
