namespace backend.DTOs.Responses;

public sealed class TutorSolicitudDto
{
    public int Id { get; init; }

    public string Nombre { get; init; } = null!;

    public string Correo { get; init; } = null!;
}
