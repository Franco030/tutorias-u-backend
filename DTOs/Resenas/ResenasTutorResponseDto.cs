namespace backend.DTOs.Resenas;

public sealed class ResenasTutorResponseDto
{
    public IReadOnlyList<ResenaResponseDto> Items { get; init; } = Array.Empty<ResenaResponseDto>();

    public int Pagina { get; init; }

    public int TamanoPagina { get; init; }

    /// <summary>Total de reseñas del tutor (no solo las de esta página).</summary>
    public int Total { get; init; }

    public decimal Promedio { get; init; }

    /// <summary>Cantidad de reseñas por puntuación, de 5 a 1 estrellas.</summary>
    public IReadOnlyList<DesgloseResenaDto> Desglose { get; init; } = Array.Empty<DesgloseResenaDto>();
}

public sealed record DesgloseResenaDto(int Puntuacion, int Cantidad);
