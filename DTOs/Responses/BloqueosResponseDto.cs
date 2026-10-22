using backend.DTOs.Requests;

namespace backend.DTOs.Responses;

public sealed class BloqueosResponseDto
{
    public IReadOnlyList<BloqueoDto> Bloqueos { get; init; } = Array.Empty<BloqueoDto>();

    // Citas pendientes o aceptadas que caen en una fecha bloqueada: bloquear
    // no las cancela, el tutor debe hacerlo si corresponde.
    public int CitasAfectadas { get; init; }
}
