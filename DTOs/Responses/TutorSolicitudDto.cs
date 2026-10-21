namespace backend.DTOs.Responses;

public sealed class TutorSolicitudDto
{
    public int Id { get; init; }

    public string Nombre { get; init; } = null!;

    public string Correo { get; init; } = null!;

    /// <summary>URL del comprobante de la solicitud más reciente.</summary>
    public string? UrlCredencial { get; init; }

    public DateTime? FechaSolicitud { get; init; }

    /// <summary>Materias que el aspirante solicitó impartir.</summary>
    public IReadOnlyList<string> Materias { get; init; } = Array.Empty<string>();
}
